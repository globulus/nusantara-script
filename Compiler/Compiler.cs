using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NusantaraScript {
  public class Compiler {
    public const int CALL_DEFAULT_JUMP_LOCATION = -1;
    private static readonly Regex IMPLICIT_ARG = new(@"\$[0-9]+", RegexOptions.Compiled);
    private static readonly Regex CONST_IDENTIFIER = new(@"^[A-Z]+(?:(_)+[A-Z]+)*$", RegexOptions.Compiled);
    private static readonly TokenType[] ASSIGNMENT_TOKEN_TYPES = { TokenType.EQUAL, TokenType.PLUS_EQUAL,
                    TokenType.MINUS_EQUAL, TokenType.STAR_EQUAL,
                    TokenType.SLASH_EQUAL, TokenType.SLASH_SLASH_EQUAL, TokenType.MOD_EQUAL };

    private bool DebugMode { get; }

    private List<byte> byteCode;
    internal List<Token> tokens;
    private int current = 0;

    private readonly Dictionary<object, int?> constTable = new();
    private readonly List<object> constList = new();
    private int constCount = 0;

    private List<Local> locals = new();
    private readonly List<Upvalue> upvalues = new();
    private int scopeDepth = -1; // Will be set to 0 with first beginScope()
    private readonly Stack<ActiveLoop> loops = new();

    private int lambdaCounter = 0;
    private int implicitVarCounter = 0;
    private int callArgumentsCounter = 0;
    private int TotalCallArgumentCounter => callArgumentsCounter + (enclosing?.TotalCallArgumentCounter ?? 0);

    private ClassCompiler currentClass = null;
    private readonly Dictionary<string, ClassCompiler> compiledClasses = new();

    private Chunk lastChunk = null;
    private readonly List<Chunk> chunks = new();

    private readonly Compiler enclosing = null;
    private FunctionKind kind;
    private string fiberName;

    // Debug info
    private CodePointer currentCodePoint;
    private readonly Dictionary<Local, Lifetime> debugInfoLocals;
    private readonly Dictionary<CodePointer, long> debugInfoLines;
    private readonly List<long> debugInfoBreakpoints;

    private int NumberOfEnclosingCompilers {
      get {
        var count = 0;
        var compiler = enclosing;
        while (compiler != null) {
          count++;
          compiler = compiler.enclosing;
        }
        return count;
      }
    }

    public Compiler(bool debugMode) {
      DebugMode = debugMode;
      debugInfoLocals = debugMode ? new Dictionary<Local, Lifetime>() : null;
      debugInfoLines = debugMode ? new Dictionary<CodePointer, long>() : null;
      debugInfoBreakpoints = debugMode ? new List<long>() : null;
    }

    private Compiler(Compiler enclosing) : this(enclosing.DebugMode) {
      this.enclosing = enclosing;
    }

    public Function Compile(List<Token> tokens, params string[] initialObjects) {
      var name = "Script " + DateTime.Now.Ticks;
      BeginScope(); // triggers the first scope, so that the VM never exists
      return CompileFunction(tokens, name, initialObjects.Length, FunctionKind.SCRIPT,
              Lifetime.Of(tokens), (compiler) => {
                foreach (var initialObject in initialObjects) {
                  DeclareLocal(initialObject, true);
                }
                compiler.fiberName = name;
                compiler.CompileInternal(false);
                compiler.EmitReturn(0, () => compiler.EmitCode(OpCode.NIL));
              });
    }

    private Function CompileFunction(List<Token> tokens, string name, int arity,
            FunctionKind kind, Lifetime lifetime, Action<Compiler> within) {
      byteCode = new List<byte>();
      this.tokens = tokens;
      current = 0;
      this.kind = kind;
      var thisName = (kind != FunctionKind.FUNCTION) ? Constants.THIS : "";
      locals.Add(new Local(thisName, 0, 0, false)); // Stores the top-level function
      BeginScope();
      if (tokens.Count > 0) {
        UpdateCurrentCodePoint(tokens[0]);
      }
      within(this);
      var keptLocals = new List<Local>(locals);
      EndScope();
      DebugInfo debugInfo = null;
      if (DebugMode) {
        locals = keptLocals;
        debugInfo = new DebugInfo(this, lifetime, debugInfoLocals.ToList(), debugInfoLines, debugInfoBreakpoints);
      }
      return new Function(name, arity, upvalues.Count, byteCode.ToArray(),
          constList.ToArray(), debugInfo);
    }

    private void CompileInternal(bool isExpr) {
      while (!IsAtEnd) {
        if (Match(TokenType.NEWLINE)) {
          continue;
        }
        if (isExpr) {
          Expression();
        } else {
          Declaration();
        }
      }
    }

    private void CompileNested(List<Token> tokens, bool isExpr) {
      var currentSave = current;
      current = 0;
      var tokensSave = this.tokens;
      tokens.Add(tokens.Last().Copy(TokenType.EOF));
      this.tokens = tokens;
      CompileInternal(isExpr);
      this.tokens = tokensSave;
      current = currentSave;
    }

    /**
    * Wraps the prepared tokens in a lambda that returns a single value, and is immediately called.
    * Used for when expressions and object comprehensions because they have side effect vars that can't
    * live in the same scope as the expression they're transpiled from.
    */
    private void CompileAsCalledLambdaWithSingleReturn(List<Token> tokens, int popsAfterReturn, Action<Compiler> block) {
      var compiler = new Compiler(this) {
        currentClass = currentClass
      };
      var f = compiler.CompileFunction(tokens, NextLambdaName(), 0, FunctionKind.LAMBDA, Lifetime.Of(tokens), wrappedCompiler => {
        block(wrappedCompiler);
        wrappedCompiler.EmitCode(OpCode.RETURN);
        wrappedCompiler.byteCode.PutInt(popsAfterReturn);
        for (var i = 0; i < popsAfterReturn; i++) {
          wrappedCompiler.EmitCode(OpCode.POP);
        }
      });
      EmitClosure(f, compiler);
      EmitCall(0);
    }

    private void BeginScope() {
      scopeDepth++;
    }

    private int EndScope(bool emitPops = true, bool keepLocals = false) {
      var popCount = DiscardLocals(scopeDepth, emitPops, keepLocals);
      scopeDepth--;
      return popCount;
    }

    private bool Declaration() {
      UpdateDebugInfo(Peek);
      if (MatchClassOpener()) {
        ClassDeclaration();
        return false;
      } else if (Match(TokenType.FN)) {
        FunDeclaration();
        return false;
      }
      return Statement();
    }

    private string ClassDeclaration() {
      var opener = Previous;
      var name = ConsumeVar("Expect class name.");
      var kind = opener.Type switch {
        TokenType.CLASS => ClassKind.CUSTOM,
        TokenType.EFFECT => ClassKind.EFFECT,
        TokenType.STANCE => ClassKind.STANCE,
        TokenType.SCENARIO => ClassKind.SCENARIO,
        TokenType.AI => ClassKind.AI,
        _ => throw new Exception("WTF"),
      };
      DeclareLocal(name, true);
      EmitClass(name, kind);
      currentClass = new ClassCompiler(Previous.Lexeme, kind, currentClass);

      if (Match(TokenType.IS)) { // superclass(es)
        do {
          var superclassName = ConsumeVar("Expect superclass name.");
          if (superclassName == name) {
            throw Error(Previous, "A class cannot inherit from itself!");
          }
          var superclass = FindCompiledClass(superclassName);
          if (superclass == null) {
            throw Error(Previous, $"Can't find a class named {superclassName}.");
          }
          if (superclass.Kind != kind) {
            throw Error(Previous, $"A class must inherit a class of the same type!");
          }
          currentClass.Superclasses.Add(superclassName);
        } while (Match(TokenType.COMMA));

        foreach (var superclassName in currentClass.Superclasses.AsEnumerable().Reverse()) {
          Variable(superclassName);
          EmitCode(OpCode.INHERIT);
        }
      }

      var hasInit = false;
      if (Match(TokenType.LEFT_BRACE)) { // class body
        while (!IsAtEnd && !Check(TokenType.RIGHT_BRACE)) {
          if (Match(TokenType.NEWLINE)) {
            continue;
          }
          if (Match(TokenType.IDENTIFIER)) {
            var fieldToken = Previous;
            var fieldName = fieldToken.Lexeme;
            currentClass?.DeclaredFields?.Add(fieldName);
            if (fieldName == Constants.INIT) {
              hasInit = true;
            }
            if (Match(TokenType.EQUAL)) {
              var list = currentClass?.InitAdditionalTokens;
              if (list != null) {
                var tokenFactory = new Token.Factory(fieldToken);
                list.Add(tokenFactory.This());
                list.Add(tokenFactory.OfType(TokenType.DOT));
                list.Add(fieldToken);
                list.Add(Previous);
                list.AddRange(ConsumeNextExpr());
                list.Add(Consume(TokenType.NEWLINE, "Expect newline after class field declaration."));
              }
            } else {
              Method(fieldName);
            }
          } else {
            throw Error(Previous, "Invalid line in class declaration.");
          }
        }
        Consume(TokenType.RIGHT_BRACE, "\"Expect '}' after class body.\"");
      } else {
        Consume(TokenType.NEWLINE, "Expect newline after empty class declaration.");
      }

      // If the class declares fields but not an "init", we need to synthesize one
      if (!hasInit) {
        Method(Constants.INIT, true);
      }

      ValidateClassFields(currentClass);

      EmitCode(OpCode.CLASS_DECLR_DONE);
      compiledClasses[name] = currentClass;
      currentClass = currentClass?.Enclosing;
      return name;
    }

    private void ValidateClassFields(ClassCompiler klass) {
      switch (klass.Kind) {
        case ClassKind.EFFECT:
          ValidatePresenceOfFields(klass, Constants.FIELD_TRIGGER, Constants.FIELD_RUN);
          break;
        case ClassKind.SCENARIO:
          ValidatePresenceOfFields(klass, Constants.FIELD_CHECK_VICTORY);
          break;
        case ClassKind.AI:
          ValidatePresenceOfFields(klass, Constants.FIELD_UPDATE);
          break;
        default:
          break;
      }
    }

    private void ValidatePresenceOfFields(ClassCompiler klass, params string[] fields) {
      var originalClass = klass;
      var hasFields = false;
      while (!hasFields && klass != null) {
        hasFields = ClassHasFields(klass, fields);
        klass = klass.Superclasses.Any() ? FindCompiledClass(klass.Superclasses.First()) : null;
      }
      if (!hasFields) {
        throw Error(Previous, $"Unable to find required fields {string.Join(", ", fields)} in {originalClass?.Name}!");
      }
    }

    private bool ClassHasFields(ClassCompiler klass, params string[] fields) {
      foreach (var field in fields) {
        if (!klass.DeclaredFields.Contains(field)) {
          return false;
        }
      }
      return true;
    }

    private string Method(string providedName = null, bool isSynthesized = false) {
      var name = providedName ?? ConsumeVar("Expect method name.");
      var kind = (name == Constants.INIT) ? FunctionKind.INIT : FunctionKind.METHOD;
      Function(kind, false, name, isSynthesized);
      EmitMethod(name);
      return name;
    }

    private string FunDeclaration() {
      var f = Function(FunctionKind.FUNCTION, true);
      return f.Name;
    }

    private Function Function(FunctionKind kind, bool declareLocal, string providedName = null, bool isSynthesized = false) {
      var declaration = (current > 0) ? Previous : tokens[0];
      var name = providedName ?? ConsumeVar("Expected an identifier for function name");
      if (declareLocal) {
        DeclareLocal(name, true);
      }
      var args = ScanArgs(kind, true);

      // the init method also initializes all the fields declared in the class
      if (kind == FunctionKind.INIT) {
        args.PrependedTokens.AddRange(currentClass.InitAdditionalTokens);
      }

      var isExprFunc = false;
      var hasBody = true;
      if (isSynthesized) {
        hasBody = false;
      } else {
        if (Match(TokenType.EQUAL_GREATER)) {
          isExprFunc = true;
        } else if (!Match(TokenType.LEFT_BRACE)) {
          hasBody = false;
          Consume(TokenType.NEWLINE, "Expect '{', '=>' or newline to start func.");
        }
      }

      if (kind == FunctionKind.LAMBDA && hasBody && args.Args.Count == 0) {
        args.Args.AddRange(ScanForImplicitArgs(declaration, isExprFunc));
      }

      var curr = current;
      var funcCompiler = new Compiler(this) {
        currentClass = currentClass
      };
      var lifetime = new Lifetime(declaration, null);
      var f = funcCompiler.CompileFunction(tokens, name, args.Args.Count, kind, lifetime, compiler => {
        compiler.fiberName = (kind == FunctionKind.FIBER) ? name : fiberName;
        compiler.current = curr;
        args.Args.ForEach(it => compiler.DeclareLocal(it));
        if (isExprFunc) {
          UpdateDebugInfo(Previous);
          lifetime.End = lifetime.Start;
          compiler.Expression();
        } else {
          if (args.PrependedTokens.Count > 0) {
            compiler.CompileNested(args.PrependedTokens, false);
          }
          if (hasBody) {
            compiler.Block(false);
          }
          lifetime.End = new CodePointer(Previous);
          if (kind == FunctionKind.INIT) {
            compiler.EmitGetLocal(Constants.THIS, null);
          } else {
            compiler.EmitCode(OpCode.NIL);
          }
        }
        compiler.ReturnOrThrowStatement(isReturn: true, consumeValue: false, checkErrors: false);
      });
      if (args.OptionalParamsStart != -1) {
        f.OptionalParamsStart = args.OptionalParamsStart;
        f.DefaultValues = args.DefaultValues.ToArray();
      }
      current = funcCompiler.current;
      EmitClosure(f, funcCompiler);
      return f;
    }

    private ArgScanResult ScanArgs(FunctionKind kind, bool allowPrepend) {
      var args = new List<string>();
      var prependedTokens = new List<Token>();
      var optionalParamsStart = -1;
      var defaultValues = new List<object>();
      if (Match(TokenType.LEFT_PAREN)) {
        if (!Check(TokenType.RIGHT_PAREN)) {
          do {
            var initAutoset = false;
            if (MatchSequence(TokenType.THIS, TokenType.DOT)) {
              if (!allowPrepend) {
                throw Error(Previous, "Autoset arguments aren't allowed here.");
              } else if (kind == FunctionKind.INIT) {
                initAutoset = true;
              } else {
                throw Error(Previous, "Autoset arguments are only allowed in initializers!");
              }
            }
            var paramName = ConsumeVar("Expect param name.");
            var tokenFactory = new Token.Factory(Previous);
            var nameToken = tokenFactory.Named(paramName);
            if (Match(TokenType.EQUAL)) {
              var defaultValue = ConsumeValue("Expected a value as default param value!");
              if (optionalParamsStart == -1) {
                optionalParamsStart = args.Count;
              }
              defaultValues.Add(defaultValue);
            }
            args.Add(paramName);
            if (initAutoset) {
              prependedTokens.Add(tokenFactory.This());
              prependedTokens.Add(tokenFactory.OfType(TokenType.DOT));
              prependedTokens.Add(nameToken);
              prependedTokens.Add(tokenFactory.OfType(TokenType.EQUAL));
              prependedTokens.Add(nameToken);
              prependedTokens.Add(tokenFactory.OfType(TokenType.NEWLINE));
            }
          } while (Match(TokenType.COMMA));
        }
        Consume(TokenType.RIGHT_PAREN, "Expected )");
      }
      return new ArgScanResult(args, prependedTokens, optionalParamsStart, defaultValues);
    }

    private bool Statement() {
      if (Match(TokenType.LEFT_BRACE)) {
        BeginScope();
        Block(false);
        EndScope();
        return false;
      }
      if (Match(TokenType.IF)) {
        IfSomething(false);
        return false;
      }
      if (Match(TokenType.WHEN)) {
        WhenSomething(false);
        return false;
      }
      if (Match(TokenType.FOR)) {
        ForStatement();
        return false;
      }
      if (Match(TokenType.WHILE)) {
        WhileStatement();
        return false;
      }
      if (Match(TokenType.RETURN)) {
        ReturnStatement();
        return true;
      }
      if (Match(TokenType.THROW)) {
        ThrowStatement();
        return true;
      }
      if (Match(TokenType.BREAK)) {
        BreakStatement();
        return true;
      }
      if (Match(TokenType.CONTINUE)) {
        ContinueStatement();
        return true;
      }
      return ExpressionStatement();
    }

    private void Block(bool isExpr) {
      var lastLineIsExpr = false;
      while (!Check(TokenType.RIGHT_BRACE)) {
        MatchAllNewlines();
        if (Peek.Type == TokenType.RIGHT_BRACE) { // allows for empty blocks
          break;
        }
        lastLineIsExpr = Declaration();
        MatchAllNewlines();
      }
      Consume(TokenType.RIGHT_BRACE, "Expect '}' after block!");
      if (isExpr && !lastLineIsExpr) {
        throw Error(Previous, "Block is not a valid expression block!");
      }
    }

    private void IfSomething(bool isExpr) {
      var opener = Previous;
      Expression();
      var ifChunk = EmitJump(OpCode.JUMP_IF_FALSE);
      EmitCode(OpCode.POP);
      CompileIfBody(opener, isExpr);
      var elseJump = EmitJump(OpCode.JUMP);
      PatchJump(ifChunk);
      EmitCode(OpCode.POP);
      MatchAllNewlines();
      if (Match(TokenType.ELSE)) {
        CompileIfBody(opener, isExpr);
      } else if (isExpr) {
        throw Error(opener, "AN if expression must have an else!");
      }
      PatchJump(elseJump);
    }

    private void CompileIfBody(Token opener, bool isExpr) {
      if (isExpr) {
        var isRealExpr = ExpressionOrExpressionBlock(Expression); // Assignments can be statements, we can't know till we parse
        if (!isRealExpr) {
          throw Error(opener, "An if expression must have a return value.");
        }
      } else {
        Statement();
      }
    }

    private bool ExpressionOrExpressionBlock(Func<bool> expressionFun) {
      return ExpressionOrExpressionBlock(new List<String>(), expressionFun);
    }

    private bool ExpressionOrExpressionBlock(List<String> localVarsToBind, Func<bool> expressionFun) {
      if (Match(TokenType.LEFT_BRACE)) {
        BeginScope();
        foreach (var name in localVarsToBind) {
          DeclareLocal(name);
        }
        Block(true);
        var popCount = EndScope(false);
        var rolledBackChunk = RollBackAndSaveLastChunk();
        EmitPopUnder(popCount);
        if (rolledBackChunk.Chunk.OpCode != OpCode.POP) {
          PushLastChunk(rolledBackChunk.Chunk);
          if (rolledBackChunk.Chunk.OpCode == OpCode.JUMP) {
            rolledBackChunk.Chunk.Data[0] = byteCode.Count + 1; // + 1 because the opcode will go first
          }
          byteCode.AddRange(rolledBackChunk.Data);
        }
        return true;
      } else {
        foreach (var name in localVarsToBind) {
          EmitCode(OpCode.POP); // Pop any leftover vars that weren't bound because we don't have a block
        }
        return expressionFun();
      }
    }

    private void WhenSomething(bool isExpr) {
      var origin = Previous;
      var isWhenThrow = Match(TokenType.THROW);
      var factory = new Token.Factory(origin);
      var whenTokens = new List<Token>();
      var usesTempVar = false;
      Token id;
      if (!isWhenThrow
              && (PeekSequence(TokenType.IDENTIFIER, TokenType.LEFT_BRACE, TokenType.NEWLINE)
                  || PeekSequence(TokenType.THIS, TokenType.LEFT_BRACE, TokenType.NEWLINE)
                  || PeekSequence(TokenType.SUPER, TokenType.LEFT_BRACE, TokenType.NEWLINE))) {
        id = Advance();
      } else {
        usesTempVar = true;
        var tempId = factory.Named(NextImplicitVarName("when"));
        whenTokens.Add(tempId);
        whenTokens.Add(factory.OfType(TokenType.EQUAL));
        whenTokens.AddRange(ConsumeUntilType(TokenType.LEFT_BRACE));
        whenTokens.Add(factory.OfType(TokenType.NEWLINE));
        if (isWhenThrow) {
          whenTokens.Add(factory.OfType(TokenType.CATCH));
        }
        id = tempId;
      }
      var first = true;
      var wroteElse = false;
      Consume(TokenType.LEFT_BRACE, "Expect a '{' after when");
      Consume(TokenType.NEWLINE, "Expect a newline after when '{'");

      Func<List<Token>> consumeConditionBlock;
      if (isExpr) {
        consumeConditionBlock = () => ConsumeUntilType(TokenType.OR_OR, TokenType.LEFT_BRACE, TokenType.EQUAL_GREATER);
      } else {
        consumeConditionBlock = () => ConsumeUntilType(TokenType.OR_OR, TokenType.LEFT_BRACE);
      }
      Func<bool> conditionAtEndBlock;
      if (isExpr) {
        conditionAtEndBlock = () => Check(TokenType.LEFT_BRACE, TokenType.EQUAL_GREATER);
      } else {
        conditionAtEndBlock = () => Check(TokenType.LEFT_BRACE);
      }

      while (!IsAtEnd && !Check(TokenType.RIGHT_BRACE)) {
        MatchAllNewlines();
        if (Match(TokenType.ELSE)) {
          wroteElse = true;
          whenTokens.Add(Previous);
          whenTokens.AddRange(ConsumeNextBlock(isExpr));
        } else if (wroteElse) {
          // We could just break when we encountered a break, but that's make for a lousy compiler
          throw Error(Previous, "'else' must be the last clause in a 'when' block");
        } else {
          Token ifToken = null;
          do {
            var op = (Match(TokenType.IS, TokenType.IN, TokenType.NOT_IN, TokenType.NOT_IS, TokenType.IF)) ? Previous : origin.Copy(TokenType.EQUAL_EQUAL);

            // Emit the beginning of the statement if it hasn't been already
            if (ifToken == null) {
              ifToken = op.Copy(TokenType.IF);
              if (first) {
                first = false;
              } else {
                whenTokens.Add(ifToken.Copy(TokenType.ELSE));
              }
              whenTokens.Add(ifToken);
            }

            // If the op type is IF, we'll just evaluate what comes after it,
            // otherwise it's a check against the id.
            if (op.Type != TokenType.IF) {
              whenTokens.Add(id);
              whenTokens.Add(op);
            }

            // Consume the rest of the condition
            whenTokens.AddRange(consumeConditionBlock());

            // If we've found an or, we just take it as-is
            if (Match(TokenType.OR_OR)) {
              whenTokens.Add(Previous);
            }
          } while (!conditionAtEndBlock());

          // Consume the statement
          whenTokens.AddRange(ConsumeNextBlock(isExpr));
        }
        MatchAllNewlines();
      }
      Consume(TokenType.RIGHT_BRACE, "Expect '}' at the end of when.");
      if (isExpr && usesTempVar) {
        whenTokens.Add(factory.OfType(TokenType.EOF));
        CompileAsCalledLambdaWithSingleReturn(whenTokens, 1, compiler => compiler.CompileInternal(true));
      } else {
        CompileNested(whenTokens, isExpr);
        if (usesTempVar) {
          DiscardLastLocal(true); // pop the temp var
        }
      }
    }

    private void ForStatement() {
      var opener = Previous;
      if (PeekSequence(TokenType.IDENTIFIER, TokenType.IN)) {
        ForeachStatement();
      } else {
        var factory = new Token.Factory(opener);
        BeginScope();
        if (!PeekSequence(TokenType.IDENTIFIER, TokenType.EQUAL)) {
          throw Error(Peek, "Expect assignment at the start of for-loop.");
        }
        Assignment();
        Consume(TokenType.NEWLINE, "Expect newline after assignment.");
        var conditionTokens = ConsumeUntilType(TokenType.NEWLINE);
        Consume(TokenType.NEWLINE, "Expect newline after condition.");
        var incrementTokens = ConsumeUntilType(TokenType.LEFT_BRACE);
        if (Peek.Type != TokenType.LEFT_BRACE) {
          throw Error(opener, "A for loop must include a block.");
        }
        var blockTokens = ConsumeNextBlock(false);
        var allTokens = new List<Token>();
        allTokens.Add(factory.OfType(TokenType.WHILE)); allTokens.AddRange(conditionTokens);
        allTokens.AddRange(blockTokens.Take(blockTokens.Count - 1)); // exclude just the final }
        allTokens.AddRange(incrementTokens);
        allTokens.Add(factory.OfType(TokenType.RIGHT_BRACE)); allTokens.Add(factory.OfType(TokenType.NEWLINE));
        CompileNested(allTokens, false);
        EndScope();
      }
    }

    private void ForeachStatement() {
      var opener = Previous;
      if (!Match(TokenType.IDENTIFIER)) {
        throw Error(Previous, "Expect identifier in foreach loop.");
      }
      var factory = new Token.Factory(opener);
      var assignmentTokens = new List<Token>();
      var id = Previous;
      assignmentTokens.Add(id);
      Consume(TokenType.IN, "Expect 'in' after identifier in 'foreach'.");
      var iterableTokens = ConsumeUntilType(TokenType.LEFT_BRACE);
      if (Peek.Type != TokenType.LEFT_BRACE) {
        throw Error(opener, "A foeach loop must include a block.");
      }
      var blockTokens = ConsumeNextBlock(false);
      var allTokens = new List<Token>();
      var iterator = factory.Named(NextImplicitVarName("iterator"));
      var eq = factory.OfType(TokenType.EQUAL);
      var lp = factory.OfType(TokenType.LEFT_PAREN);
      var rp = factory.OfType(TokenType.RIGHT_PAREN);
      var dot = factory.OfType(TokenType.DOT);
      var nl = factory.OfType(TokenType.NEWLINE);
      var ifToken = factory.OfType(TokenType.IF);
      var eqEq = factory.OfType(TokenType.EQUAL_EQUAL);
      var nil = factory.OfType(TokenType.NULL);
      var checkTokens = new List<Token> {
                id, eqEq, nil
            };
      allTokens.Add(iterator); allTokens.Add(eq); allTokens.Add(lp);
      allTokens.AddRange(iterableTokens);
      allTokens.Add(rp); allTokens.Add(dot); allTokens.Add(factory.Named(Constants.ITERATE));
      allTokens.Add(lp); allTokens.Add(rp); allTokens.Add(nl);
      allTokens.Add(factory.OfType(TokenType.WHILE)); allTokens.Add(iterator);
      allTokens.Add(factory.OfType(TokenType.BANG_EQUAL)); allTokens.Add(nil);
      for (var i = 0; i < blockTokens.Count; i++) {
        allTokens.Add(blockTokens[i]);
        if (i == 0) {
          allTokens.Add(nl); allTokens.AddRange(assignmentTokens);
          allTokens.AddRange(new List<Token> {
                        eq, iterator, dot, factory.Named(Constants.NEXT), lp, rp, nl, ifToken
                    });
          allTokens.AddRange(checkTokens);
          allTokens.Add(factory.OfType(TokenType.BREAK));
          allTokens.Add(nl);
        }
      }
      CompileNested(allTokens, false);
      // Remove the iterator as it isn't needed anymore
      DiscardLastLocal(true);
    }

    private void WhileStatement() {
      var start = byteCode.Count;
      loops.Push(new ActiveLoop(start, scopeDepth));
      Expression();
      var skipChunk = EmitJump(OpCode.JUMP_IF_FALSE);
      EmitCode(OpCode.POP);
      Statement();
      EmitJump(OpCode.JUMP, start);
      var end = PatchJump(skipChunk);
      PatchLoopBreaks(end);
      EmitCode(OpCode.POP);
    }

    private void PatchLoopBreaks(int end) {
      var breaksToPatch = loops.Pop().Breaks;
      // Set to jump 1 after end to skip the final POP as it already happened in the loop body
      var skip = end + 1;
      foreach (var pos in breaksToPatch) {
        pos.Data[1] = skip;
        byteCode.SetInt(skip, (int)pos.Data[0]);
      }
    }

    private void ReturnStatement() {
      ReturnOrThrowStatement(isReturn: true, consumeValue: true);
    }

    private void ThrowStatement() {
      ReturnOrThrowStatement(isReturn: false, consumeValue: true);
    }

    private void ReturnOrThrowStatement(bool isReturn, bool consumeValue, bool checkErrors = true) {
      if (checkErrors && kind == FunctionKind.INIT) {
        throw Error(Previous, $"Can't {(isReturn ? "return" : "throw")} from init!");
      }
      if (consumeValue) {
        ConsumeReturnValue(false, isReturn);
      }
      if (isReturn) {
        EmitReturn(0, () => { });
      } else {
        EmitThrow();
      }
      // After return/throw is emitted, we need to close the scope, so we emit the number of additional
      // instructions to interpret before we actually return from call frame
      var pos = byteCode.Count;
      byteCode.PutInt(0);
      var popCount = DiscardLocals(scopeDepth, true, true);
      byteCode.SetInt(popCount, pos);
    }

    private void BreakStatement() {
      if (loops.Count == 0) {
        throw Error(Previous, "Cannot 'break' outside of a loop!");
      }
      var activeLoop = loops.Peek();
      // Discards scopes up to the level of loop statement (hence + 1)
      for (var depth = scopeDepth; depth >= activeLoop.Depth + 1; depth--) {
        DiscardLocals(depth, true, true);
      }
      var chunk = EmitJump(OpCode.JUMP);
      activeLoop.Breaks.Add(chunk);
    }

    private void ContinueStatement() {
      if (loops.Count == 0) {
        throw Error(Previous, "Cannot 'continue' outside of a loop!");
      }
      var activeLoop = loops.Peek();
      // Discards scopes up to the level of loop statement (hence + 1)
      for (var depth = scopeDepth; depth >= activeLoop.Depth + 1; depth--) {
        DiscardLocals(depth, true, true);
      }
      EmitJump(OpCode.JUMP, loops.Peek().Start);
    }

    private bool ExpressionStatement() {
      try {
        var shouldPop = Expression();
        if (shouldPop) {
          EmitCode(OpCode.POP);
        }
        return shouldPop;
      } catch (StatementDeepDown) {
        return false;
      }
    }

    private bool Expression() {
      if (Match(TokenType.CATCH)) {
        EmitCode(OpCode.UNBOX_THROWN);
        return true;
      }
      return Assignment();
    }

    private bool Assignment() {
      FirstNonAssignment(false); // left-hand side
      if (Match(ASSIGNMENT_TOKEN_TYPES)) {
        var equals = Previous;
        if (lastChunk?.OpCode == OpCode.GET_SUPER) {
          throw Error(equals, "Setters aren't allowed with 'super'.");
        }
        if (equals.Type == TokenType.EQUAL) {
          if (lastChunk?.OpCode == OpCode.GET_PROP) {
            /* What happens here is as follows:
            1. Remove the GET_PROP chunk as it'll get replaced with SET_PROP down the line.
            2. If we're in an INIT and we're looking at setting a new var (CONST_ID), store that into declaredField.
            3. Emit the value to set to.
            4. Add the declaredField (if it exists) to declaredFields of enclosing compiler (as its the one that's compiling the class). The reason why that's done here is to prevent false self. markings in the setting expression, e.g, @from = from would wrongly be identified as @from = @from if we added declaredField to declaredFields immediately.
            */
            RollBackLastChunk();
            var declaredField = (lastChunk?.OpCode == OpCode.CONST_ID && kind == FunctionKind.INIT)
                ? (string)lastChunk.Data[1]
                : null;
            FirstNonAssignment(true);
            if (declaredField != null) {
              enclosing?.currentClass?.DeclaredFields?.Add(declaredField);
            }
            EmitCode(OpCode.SET_PROP);
          } else {
            if (lastChunk?.OpCode == OpCode.GET_UPVALUE || lastChunk?.OpCode == OpCode.GET_LOCAL) {
              Reassignment(equals); // Set local/upvalue
            } else { // Declare local
              DeclareLocal((string)lastChunk.Data[1]);
              RollBackLastChunk();
              FirstNonAssignment(true); // push the value on the stack
            }
          }
        } else {
          if (lastChunk?.OpCode == OpCode.GET_PROP) {
            RollBackLastChunk(); // this isn't a get but an update, roll back
            var op = OpCodeForCompoundAssignment(equals.Type);
            FirstNonAssignment(true); // right-hand side
            EmitUpdateProp(op);
          } else {
            Reassignment(equals);
          }
        }
        return false;
      }
      return true;
    }

    private void Reassignment(Token equals) {
      object variable = (lastChunk?.OpCode) switch {
        OpCode.GET_LOCAL => (Local)lastChunk.Data[1],
        OpCode.GET_UPVALUE => (int)lastChunk.Data[1],
        _ => throw Error(equals, "Assigning to undeclared var!"),
      };
      var isConst = (variable is Local local) ? local.IsConst : upvalues[(int)variable].IsConst;
      if (isConst) {
        throw Error(Previous, "Can't assign to a const!");
      }
      if (equals.Type == TokenType.EQUAL) {
        RollBackLastChunk(); // It'll just be a set, compound-assigns reuse the already emitted GET_LOCAL
      }
      FirstNonAssignment(true); // right-hand side
      if (equals.Type != TokenType.EQUAL) {
        EmitCode(OpCodeForCompoundAssignment(equals.Type));
      }
      EmitSet(variable);
    }

    // irsoa = is right side of assignment
    private void FirstNonAssignment(bool irsoa) { // first expression type that's not an assignment
      ThrownCoalescence(irsoa);
    }

    private void ThrownCoalescence(bool irsoa) {
      Or(irsoa);
      if (Match(TokenType.QUESTION_BANG)) {
        var elseChunk = EmitJump(OpCode.JUMP_IF_THROWN);
        var endChunk = EmitJump(OpCode.JUMP);
        PatchJump(elseChunk);
        if (Match(TokenType.THROW)) {
          EmitThrow(); // keep the thrown value on stack and rethrow it
        } else {
          EmitCode(OpCode.POP);
          Or(irsoa);
        }
        PatchJump(endChunk);
      }
    }

    private void Or(bool irsoa) {
      And(irsoa);
      while (Match(TokenType.OR_OR)) {
        var elseChunk = EmitJump(OpCode.JUMP_IF_FALSE);
        var endChunk = EmitJump(OpCode.JUMP);
        PatchJump(elseChunk);
        EmitCode(OpCode.POP);
        And(irsoa);
        PatchJump(endChunk);
      }
    }

    private void And(bool irsoa) {
      Equality(irsoa);
      while (Match(TokenType.AND_AND)) {
        var endChunk = EmitJump(OpCode.JUMP_IF_FALSE);
        EmitCode(OpCode.POP);
        Equality(irsoa);
        PatchJump(endChunk);
      }
    }

    private void Equality(bool irsoa) {
      Comparison(irsoa);
      while (Match(TokenType.BANG_EQUAL, TokenType.EQUAL_EQUAL, TokenType.IS, TokenType.NOT_IS, TokenType.IN, TokenType.NOT_IN)) {
        var op = Previous;
        Comparison(irsoa);
        switch (op.Type) {
          case TokenType.EQUAL_EQUAL:
            EmitCode(OpCode.EQ);
            break;
          case TokenType.BANG_EQUAL:
            EmitCode(OpCode.EQ);
            EmitCode(OpCode.INVERT);
            break;
          case TokenType.IS:
            EmitCode(OpCode.IS);
            break;
          case TokenType.NOT_IS:
            EmitCode(OpCode.IS);
            EmitCode(OpCode.INVERT);
            break;
          case TokenType.IN:
            EmitCode(OpCode.HAS);
            break;
          case TokenType.NOT_IN:
            EmitCode(OpCode.HAS);
            EmitCode(OpCode.INVERT);
            break;
          default:
            throw new Exception("WTF");
        }
      }
    }

    private void Comparison(bool irsoa) {
      Range(irsoa);
      while (Match(TokenType.GREATER, TokenType.GREATER_EQUAL, TokenType.LESS, TokenType.LESS_EQUAL)) {
        var op = Previous;
        Range(irsoa);
        switch (op.Type) {
          case TokenType.GREATER:
            EmitCode(OpCode.GT);
            break;
          case TokenType.GREATER_EQUAL:
            EmitCode(OpCode.GE);
            break;
          case TokenType.LESS:
            EmitCode(OpCode.LT);
            break;
          case TokenType.LESS_EQUAL:
            EmitCode(OpCode.LE);
            break;
          default:
            throw new Exception("WTF");
        }
      }
    }

    private void Range(bool irsoa) {
      Addition(irsoa);
      while (Match(TokenType.DOT_DOT, TokenType.DOT_DOT_DOT)) {
        var op = Previous;
        Addition(irsoa);
        if (op.Type == TokenType.DOT_DOT) {
          // if it's range until, emit a - 1 also
          EmitInt(1);
          EmitCode(OpCode.SUBTRACT);
        }
        EmitCode(OpCode.RANGE_TO);
      }
    }

    private void Addition(bool irsoa) {
      Multiplication(irsoa);
      while (Match(TokenType.MINUS, TokenType.PLUS)) {
        var op = Previous;
        Multiplication(irsoa);
        EmitCode((op.Type == TokenType.PLUS) ? OpCode.ADD : OpCode.SUBTRACT);
      }
    }

    private void Multiplication(bool irsoa) {
      NilCoalescence(irsoa);
      while (Match(TokenType.SLASH, TokenType.SLASH_SLASH, TokenType.STAR, TokenType.MOD)) {
        var op = Previous;
        NilCoalescence(irsoa);
        switch (op.Type) {
          case TokenType.SLASH:
            EmitCode(OpCode.DIVIDE);
            break;
          case TokenType.SLASH_SLASH:
            EmitCode(OpCode.DIVIDE_INT);
            break;
          case TokenType.STAR:
            EmitCode(OpCode.MULTIPLY);
            break;
          case TokenType.MOD:
            EmitCode(OpCode.MOD);
            break;
          default:
            throw new Exception("WTF");
        }
      }
    }

    private void NilCoalescence(bool irsoa) {
      Unary(irsoa);
      while (Match(TokenType.QUESTION_QUESTION)) {
        var elseChunk = EmitJump(OpCode.JUMP_IF_NIL);
        var endChunk = EmitJump(OpCode.JUMP);
        PatchJump(elseChunk);
        EmitCode(OpCode.POP);
        Unary(irsoa);
        PatchJump(endChunk);
      }
    }

    private void Unary(bool irsoa) {
      if (Match(TokenType.BANG, TokenType.MINUS)) {
        var op = Previous;
        Unary(irsoa);
        EmitCode((op.Type == TokenType.BANG) ? OpCode.INVERT : OpCode.NEGATE);
      } else {
        Call(irsoa);
      }
    }

    private void Call(bool irsoa) {
      Primary(irsoa, true);
      // Every iteration below decrements first, so it needs to start at 2 to be > 0 for the first check
      var superCount = (lastChunk?.OpCode == OpCode.SUPER) ? 2 : 1;
      while (true) {
        superCount--;
        var wasSuper = superCount > 0;
        // if (wasSuper) {
        //     RollBackLastChunk();
        // }
        if (Match(TokenType.LEFT_PAREN)) {
          FinishCall();
        } else if (Match(TokenType.DOT, TokenType.QUESTION_DOT, TokenType.DOT_COMMA,
                TokenType.QUESTION_DOT_COMMA, TokenType.COLON_COLON)) {
          var op = Previous.Type;
          var nullSafeCall = (op == TokenType.QUESTION_DOT || op == TokenType.QUESTION_DOT_COMMA);
          var cascadeCall = (op == TokenType.DOT_COMMA || op == TokenType.QUESTION_DOT_COMMA);
          if (Match(TokenType.LEFT_PAREN)) {
            Expression();
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after evaluated getter.");
            FinishGet(wasSuper);
            continue; // Go the the next iteration to prevent INVOKE shenanigans
          } else {
            Primary(false, false);
          }

          if ((Match(TokenType.LEFT_PAREN) || !ASSIGNMENT_TOKEN_TYPES.Contains(Peek.Type))
                  && lastChunk?.OpCode != OpCode.CONST_INT
                  && lastChunk?.OpCode != OpCode.GET_UPVALUE
                  && op != TokenType.COLON_COLON) { // Check invoke
            var name = (string)lastChunk.Data[(lastChunk?.OpCode == OpCode.GET_LOCAL) ? 0 : 1];
            RollBackLastChunk();
            var argCount = (Previous.Type == TokenType.LEFT_PAREN) ? CallArgList() : 0;
            EmitInvoke(name, argCount, wasSuper, nullSafeCall, cascadeCall);
          } else {
            if (lastChunk?.OpCode == OpCode.GET_LOCAL || lastChunk?.OpCode == OpCode.GET_UPVALUE) {
              var name = (string)lastChunk.Data[0];
              RollBackLastChunk();
              EmitId(name);
            }
            FinishGet(wasSuper);
          }
        } else {
          break;
        }
      }
    }

    private void FinishCall() {
      CheckIfUnidentified();
      var argCount = CallArgList();
      EmitCall(argCount);
    }

    private int CallArgList() {
      callArgumentsCounter++;
      var count = 0;
      if (!Check(TokenType.RIGHT_PAREN)) {
        do {
          count++;
          MatchSequence(TokenType.IDENTIFIER, TokenType.COLON); // allows for named params
          Expression();
          CheckIfUnidentified();
        } while (Match(TokenType.COMMA));
      }
      Consume(TokenType.RIGHT_PAREN, "Expect ')' after arguments.");
      callArgumentsCounter--;
      return count;
    }

    private void FinishGet(bool wasSuper) {
      if (wasSuper) {
        if (lastChunk?.OpCode != OpCode.CONST_ID) {
          throw Error(Previous, "'super' get can only involve an identifier.");
        }
        EmitCode(OpCode.GET_SUPER);
      } else {
        EmitCode(OpCode.GET_PROP);
      }
    }

    private void Primary(bool irsoa, bool checkImplicitThis) {
      if (Match(TokenType.FALSE)) {
        EmitCode(OpCode.FALSE);
      } else if (Match(TokenType.TRUE)) {
        EmitCode(OpCode.TRUE);
      } else if (Match(TokenType.NULL)) {
        EmitCode(OpCode.NIL);
      } else if (Match(TokenType.STRING)) {
        EmitConst(((Str)Previous.Literal).Value);
      } else if (Match(TokenType.NUMBER)) {
        var token = Previous;
        if (token.Literal is Int i) {
          EmitInt(i.Value);
        } else {
          EmitFloat(((Float)token.Literal).Value);
        }
      } else if (Match(TokenType.SUPER)) {
        if (currentClass == null) {
          throw Error(Previous, "Cannot use 'super' outside of class.");
        } else if (currentClass?.Superclasses?.Any() != true) {
          throw Error(Previous, "Cannot use 'super' in a class that doesn't have a superclass.");
        }
        var superclass = currentClass?.Superclasses?.First();
        EmitSuper(superclass);
      } else if (Match(TokenType.THIS)) {
        if (currentClass == null) {
          throw Error(Previous, "Cannot use 'this' outside of class.");
        }
        Variable();
      } else if (Match(TokenType.LEFT_BRACKET)) {
        ObjectLiteral();
      } else if (Match(TokenType.FN) || Peek.Type == TokenType.EQUAL_GREATER) {
        Function(FunctionKind.LAMBDA, false, NextLambdaName());
      } else if (Match(TokenType.IDENTIFIER)) {
        var name = Previous.Lexeme;
        if (checkImplicitThis && ValidateImplicitThis(currentClass, name)) {
          Variable(Constants.THIS);
          EmitId(name);
          FinishGet(false);
        } else {
          Variable(name);
          if (irsoa) {
            CheckIfUnidentified();
          }
        }
      } else if (Match(TokenType.LEFT_PAREN)) {
        Expression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after expression.");
      } else if (Match(TokenType.IF)) {
        IfSomething(true);
      } else if (Match(TokenType.WHEN)) {
        WhenSomething(true);
      } else {
        throw Error(Peek, "Expect expression.");
      }
    }

    private void Variable(string providedName = null) {
      var name = providedName ?? Previous.Lexeme;
      var local = FindLocal(this, name);
      if (local != null) {
        EmitGetLocal(name, local);
      } else {
        var upvalue = ResolveUpvalue(this, name)?.Item1;
        if (upvalue != null) {
          EmitGetUpvalue(name, (Upvalue)upvalue);
        } else {
          EmitId(name);
        }
      }
    }

    private void ObjectLiteral() {
      var count = 0;
      bool? isList = null;
      if (Match(TokenType.RIGHT_BRACKET)) { // empty list literal
        isList = true;
      } else if (!MatchSequence(TokenType.COLON, TokenType.RIGHT_BRACKET)) {// [:] is an empty object literal
        MatchAllNewlines();
        do {
          MatchAllNewlines();
          isList ??= !(PeekSequence(TokenType.IDENTIFIER, TokenType.COLON) || PeekSequence(TokenType.STRING, TokenType.COLON));
          if (isList == true) {
            Expression();
          } else {
            EmitConst(Match(TokenType.STRING)
                ? ((Str)Previous.Literal).Value
                : ConsumeVar("Expect identifier or string as object key."));
            Consume(TokenType.COLON, "Expect ':' between object key and value pair.");
            Expression();
          }
          MatchAllNewlines();
          count++;
        } while (Match(TokenType.COMMA));
        MatchAllNewlines();
        Consume(TokenType.RIGHT_BRACKET, "Expect ']' at the end of object.");
      }
      EmitObjectLiteral(isList == true, count);
    }

    private Token Consume(TokenType type, string message) {
      if (Check(type)) {
        return Advance();
      } else {
        throw Error(Peek, message);
      }
    }

    private string ConsumeVar(string message) => Consume(TokenType.IDENTIFIER, message).Lexeme;

    private object ConsumeValue(string message) {
      if (Match(TokenType.FALSE)) {
        return false;
      }
      if (Match(TokenType.TRUE)) {
        return true;
      }
      if (Match(TokenType.NULL)) {
        return Nil.INSTANCE;
      }
      if (Match(TokenType.NUMBER)) {
        var num = Previous.Literal;
        if (num is Int i) {
          return i.Value;
        } else {
          return ((Float)num).Value;
        }
      }
      if ((Match(TokenType.STRING))) {
        return ((Str)Previous.Literal).Value;
      }
      throw Error(Peek, message);
    }

    private void ConsumeReturnValue(bool emitNil, bool isReturn) {
      if ((Match(TokenType.NEWLINE))) {
        if (emitNil) {
          EmitCode(OpCode.NIL);
        }
      } else {
        Expression();
        CheckIfUnidentified();
        if (Peek.Type != TokenType.RIGHT_BRACE) {
          Consume(TokenType.NEWLINE, $"Expect newline after {(isReturn ? "return" : "throw")} value.");
        }
      }
    }

    private List<Token> ConsumeNextBlock(bool isExpr) {
      if (isExpr && Match(TokenType.EQUAL_GREATER)) {
        return ConsumeNextExpr();
      } else {
        var start = current;
        var braceCount = 0;
        var end = 0;
        for (var i = start; i < tokens.Count; i++) {
          var type = tokens[i].Type;
          if (type == TokenType.LEFT_BRACE) {
            braceCount++;
          } else if (type == TokenType.RIGHT_BRACE) {
            braceCount--;
            if (braceCount == 0) {
              end = i;
              break;
            }
          }
        }
        current = end + 1;
        var sublist = tokens.GetRange(start, current - start);
        MatchAllNewlines();
        return sublist;
      }
    }

    private List<Token> ConsumeArgList() {
      var start = current;
      var parenCount = 0;
      var end = 0;
      for (var i = start; i < tokens.Count; i++) {
        var type = tokens[i].Type;
        if (type == TokenType.LEFT_PAREN) {
          parenCount++;
        } else if (type == TokenType.RIGHT_PAREN) {
          parenCount--;
          if (parenCount == 0) {
            end = i;
            break;
          }
        }
      }
      current = end + 1;
      return tokens.GetRange(start, current - start);
    }

    private List<Token> ScanNextExpr() {
      var start = current;
      var end = 0;
      var parenCount = 0;
      var bracketCount = 0;
      for (var i = start; i < tokens.Count; i++) {
        var type = tokens[i].Type;
        if (type == TokenType.RIGHT_BRACE || type == TokenType.EOF
                || (type == TokenType.NEWLINE && parenCount == 0 && bracketCount == 0)) {
          end = i;
          break;
        } else if (type == TokenType.LEFT_PAREN) {
          parenCount++;
        } else if (type == TokenType.LEFT_BRACKET) {
          bracketCount++;
        } else if (type == TokenType.RIGHT_PAREN) {
          parenCount--;
          if (parenCount < 0) {
            end = i;
            break;
          }
        } else if (type == TokenType.RIGHT_BRACKET) {
          bracketCount--;
          if (bracketCount < 0) {
            end = i;
            break;
          }
        } else if (type == TokenType.COMMA) {
          if (parenCount == 0 && bracketCount == 0) {
            end = i;
            break;
          }
        }
      }
      return tokens.GetRange(start, end - start);
    }

    private List<Token> ConsumeNextExpr() {
      var tokens = ScanNextExpr();
      current += tokens.Count;
      return tokens;
    }

    private List<Token> ConsumeUntilType(params TokenType[] types) {
      var start = current;
      while (!IsAtEnd && !Check(types)) {
        if (Peek.Type == TokenType.NEWLINE) {
          throw Error(Peek, "Unreachable token types!");
        }
        current++;
      }
      return tokens.GetRange(start, current - start);
    }

    private List<Token> ConsumeUntilRightBracket() {
      var start = current;
      var bracketCount = 0;
      while (!IsAtEnd) {
        if (Check(TokenType.LEFT_BRACKET)) {
          bracketCount++;
        } else if (Check(TokenType.RIGHT_BRACKET)) {
          bracketCount--;
          if (bracketCount == -1) {
            break;
          }
        } else if (Peek.Type == TokenType.NEWLINE) {
          throw Error(Peek, "Unreachable right bracket!");
        }
        current++;
      }
      return tokens.GetRange(start, current - start);
    }

    private bool Match(params TokenType[] types) {
      foreach (var type in types) {
        if (Check(type)) {
          Advance();
          return true;
        }
      }
      return false;
    }

    private void MatchAllNewlines() {
      while (Match(TokenType.NEWLINE)) { }
    }

    private bool MatchClassOpener() => Match(TokenType.CLASS, TokenType.EFFECT, TokenType.STANCE, TokenType.SCENARIO, TokenType.AI);

    private bool MatchSequence(params TokenType[] types) {
      for (var i = 0; i < types.Length; i++) {
        var index = current + i;
        if (index >= tokens.Count) {
          return false;
        }
        if (tokens[index].Type != types[i]) {
          return false;
        }
      }
      for (var i = 0; i < types.Length; i++) {
        Advance();
      }
      return true;
    }

    private void Assert(bool condition) {
      if (!condition) {
        throw new Exception("Assertion failed!");
      }
    }

    private bool Check(params TokenType[] tokenTypes) {
      foreach (var tokenType in tokenTypes) {
        if (IsAtEnd && tokenType == TokenType.EOF) {
          return true;
        }
        if (Peek.Type == tokenType) {
          return true;
        }
      }
      return false;
    }

    private Token Advance() {
      if (!IsAtEnd) {
        current++;
      }
      return Previous;
    }

    private bool IsAtEnd => current == tokens.Count || Peek.Type == TokenType.EOF;

    private Token Peek => tokens[current];

    private bool PeekSequence(params TokenType[] tokenTypes) {
      if (current + tokenTypes.Length >= tokens.Count) {
        return false;
      }
      for (var i = 0; i < tokenTypes.Length; i++) {
        if (tokens[current + i].Type != tokenTypes[i]) {
          return false;
        }
      }
      return true;
    }

    private Token Previous => tokens[current - 1];

    private void RollBack(int by = 1) {
      for (var i = 0; i < by; i++) {
        byteCode.RemoveAt(byteCode.Count - 1);
      }
    }

    private void RollBackLastChunk() {
      RollBack(lastChunk.Size);
      chunks.RemoveAt(chunks.Count - 1);
      lastChunk = (chunks.Count == 0) ? null : chunks.Last();
    }

    private RolledBackChunk RollBackAndSaveLastChunk() {
      var chunk = lastChunk;
      var size = byteCode.Count;
      var data = new List<byte>();
      for (var i = size - chunk.Size; i < size; i++) {
        data.Add(byteCode[i]);
      }
      RollBackLastChunk();
      return new RolledBackChunk(chunk, data);
    }

    private CompilerError Error(Token token, string message) {
      return new CompilerError(BundleTokenWithMessage(token, message) + TokenPatcher.Patch(tokens, token));
    }

    private string BundleTokenWithMessage(Token token, string message) {
      return $"[\"{token.File}\" line {token.Line}] At {token.Type}: {message}\n";
    }

    private int ConstIndex(object c) {
      var constant = constTable.GetValue(c);
      if (constant != null) {
        return (int)constant;
      } else {
        constTable[c] = constCount++;
        constList.Add(c);
        return constCount - 1;
      }
    }

    private void EmitCode(OpCode opCode) {
      var size = byteCode.Put(opCode);
      PushLastChunk(new Chunk(opCode, size));
    }

    private void EmitPopUnder(int count) {
      var size = byteCode.Put(OpCode.POP_UNDER);
      size += byteCode.PutInt(count);
      PushLastChunk(new Chunk(OpCode.POP_UNDER, size, count));
    }

    private void EmitInt(long l) {
      var opCode = OpCode.CONST_INT;
      var size = byteCode.Put(opCode);
      size += byteCode.PutLong(l);
      PushLastChunk(new Chunk(opCode, size, l));
    }

    private void EmitFloat(double d) {
      var opCode = OpCode.CONST_FLOAT;
      var size = byteCode.Put(opCode);
      size += byteCode.PutDouble(d);
      PushLastChunk(new Chunk(opCode, size, d));
    }

    private void EmitId(String id) {
      EmitIdOrConst(OpCode.CONST_ID, id);
    }

    private void EmitConst(object c) {
      EmitIdOrConst(OpCode.CONST, c);
    }

    private void EmitIdOrConst(OpCode opCode, object c) {
      var size = byteCode.Put(opCode);
      var constIndex = ConstIndex(c);
      size += byteCode.PutInt(constIndex);
      PushLastChunk(new Chunk(opCode, size, constIndex, c));
    }

    private void EmitClosure(Function f, Compiler funcCompiler) {
      var opCode = OpCode.CLOSURE;
      var size = byteCode.Put(opCode);
      var constIndex = ConstIndex(f);
      size += byteCode.PutInt(constIndex);
      for (var i = 0; i < f.UpvalueCount; i++) {
        var upvalue = funcCompiler.upvalues[i];
        size += byteCode.Put((byte)(upvalue.IsLocal ? 1 : 0));
        size += byteCode.PutInt(upvalue.Sp);
        size += byteCode.PutInt(ConstIndex(upvalue.FiberName));
      }
      PushLastChunk(new Chunk(opCode, size, constIndex, f));
    }

    private void EmitClass(string name, ClassKind kind) {
      var opCode = OpCode.CLASS;
      var size = byteCode.Put(opCode);
      size += byteCode.Put(kind.Byte());
      var constIndex = ConstIndex(name);
      size += byteCode.PutInt(constIndex);
      PushLastChunk(new Chunk(opCode, size, kind, constIndex, name));
    }

    private void EmitMethod(string name) {
      var opCode = OpCode.METHOD;
      var size = byteCode.Put(opCode);
      var constIndex = ConstIndex(name);
      size += byteCode.PutInt(constIndex);
      PushLastChunk(new Chunk(opCode, size, constIndex, name));
    }

    private void EmitField(string name) {
      var opCode = OpCode.FIELD;
      var size = byteCode.Put(opCode);
      var constIndex = ConstIndex(name);
      size += byteCode.PutInt(constIndex);
      PushLastChunk(new Chunk(opCode, size, constIndex, name));
    }

    private void EmitGetLocal(string name, Local local) {
      var opCode = OpCode.GET_LOCAL;
      var size = byteCode.Put(opCode);
      size += byteCode.PutInt(local?.Sp ?? 0);
      PushLastChunk(new Chunk(opCode, size, name, (object)local ?? name));
    }

    private void EmitGetUpvalue(string name, Upvalue upvalue) {
      var opCode = OpCode.GET_UPVALUE;
      var index = upvalues.IndexOf(upvalue);
      var size = byteCode.Put(opCode);
      size += byteCode.PutInt(index);
      PushLastChunk(new Chunk(opCode, size, name, index, upvalue));
    }

    private void EmitSet(object it) {
      if (it is Local l) {
        EmitSetLocal(l);
      } else if (it is int i) {
        EmitSetUpvalue(i);
      } else {
        throw new Exception("WTF");
      }
    }

    private void EmitSetLocal(Local local) {
      var opCode = OpCode.SET_LOCAL;
      var size = byteCode.Put(opCode);
      size += byteCode.PutInt(local.Sp);
      PushLastChunk(new Chunk(opCode, size, local));
    }

    private void EmitSetUpvalue(int index) {
      var opCode = OpCode.SET_UPVALUE;
      var size = byteCode.Put(opCode);
      size += byteCode.PutInt(index);
      PushLastChunk(new Chunk(opCode, size, index, upvalues[index]));
    }

    private void EmitUpdateProp(OpCode op) {
      var opCode = OpCode.UPDATE_PROP;
      var size = byteCode.Put(opCode);
      size += byteCode.Put(op);
      PushLastChunk(new Chunk(opCode, size, op));
    }

    private Chunk EmitJump(OpCode opCode, int? location = null) {
      var size = byteCode.Put(opCode);
      var offset = byteCode.Count;
      var skip = location ?? 0;
      size += byteCode.PutInt(skip);
      var chunk = new Chunk(opCode, size, offset, skip);
      PushLastChunk(chunk);
      return chunk;
    }

    private int PatchJump(Chunk chunk) {
      var offset = (int)chunk.Data[0];
      var skip = byteCode.Count;
      byteCode.SetInt(skip, offset);
      chunk.Data[1] = skip;
      return skip;
    }

    private void EmitCall(int argCount) {
      var opCode = OpCode.CALL;
      var size = byteCode.Put(opCode);
      size += byteCode.PutInt(argCount);
      PushLastChunk(new Chunk(opCode, size, argCount));
    }

    private void EmitInvoke(string name, int argCount, bool wasSuper, bool safe, bool cascade) {
      var opCode = wasSuper ? OpCode.SUPER_INVOKE : OpCode.INVOKE;
      var size = byteCode.Put(opCode);
      var constIndex = ConstIndex(name);
      size += byteCode.PutInt(constIndex);
      size += byteCode.PutInt(argCount);
      size += byteCode.PutBool(safe);
      size += byteCode.PutBool(cascade);
      PushLastChunk(new Chunk(opCode, size, name, argCount, safe, cascade, constIndex));
    }

    private void EmitSuper(string superclass) {
      var opCode = OpCode.SUPER;
      var size = byteCode.Put(opCode);
      var constIndex = ConstIndex(superclass);
      size += byteCode.PutInt(constIndex);
      PushLastChunk(new Chunk(opCode, size, superclass, constIndex));
    }

    private void EmitReturn(int skipsToEmit, Action value) {
      EmitReturnOrThrow(OpCode.RETURN, skipsToEmit, value);
    }

    private void EmitThrow() {
      EmitReturnOrThrow(OpCode.THROW, 0, () => { });
    }

    private void EmitReturnOrThrow(OpCode opCode, int skipsToEmit, Action value) {
      value();
      var size = byteCode.Put(opCode);
      size += byteCode.PutInt(skipsToEmit);
      PushLastChunk(new Chunk(opCode, size));
    }

    private void EmitObjectLiteral(bool isList, int propCount) {
      var opCode = isList ? OpCode.LIST : OpCode.OBJECT;
      var size = byteCode.Put(opCode);
      size += byteCode.PutInt(propCount);
      PushLastChunk(new Chunk(opCode, size, propCount));
    }

    private void PushLastChunk(Chunk chunk) {
      chunk.Pos = byteCode.Count - chunk.Size;
      lastChunk = chunk;
      chunks.Add(chunk);
    }

    private Local DeclareLocal(string name, bool isConst = false) {
      var end = locals.Count;
      for (var i = end - 1; i >= 0; i--) {
        var local = locals[i];
        if (local.Depth < scopeDepth) {
          break;
        }
        if (local.Name == name) {
          throw Error(Previous, $"Variable {name} is already declared in scope!");
        }
      }
      var l = new Local(name, end, scopeDepth, false);
      if (isConst) {
        l.IsConst = true;
      }
      locals.Add(l);
      debugInfoLocals?.Add(l, new Lifetime(currentCodePoint, null));
      return l;
    }

    private Local FindLocal(Compiler compiler, string name) {
      for (var i = compiler.locals.Count - 1; i >= 0; i--) {
        var local = compiler.locals[i];
        if (local.Name == name) {
          return local;
        }
      }
      return null;
    }

    private int DiscardLocals(int depth, bool emitPops = true, bool keepLocals = false) {
      var popCount = 0;
      var i = locals.FindLastIndex(l => l.Depth == depth);
      while (i >= 0 && locals[i].Depth == depth) {
        if (emitPops) {
          popCount++;
          if (locals[i].IsCaptured) {
            EmitCode(OpCode.CLOSE_UPVALUE);
          } else {
            EmitCode(OpCode.POP);
          }
        }
        if (!keepLocals) {
          locals.RemoveAt(i);
        }
        i--;
      }
      return popCount;
    }

    private void DiscardLastLocal(bool pop) {
      var end = locals.Count - 1;
      locals.RemoveAt(end);
      if (pop) {
        EmitCode(OpCode.POP);
      }
    }

    private (Upvalue, int)? ResolveUpvalue(Compiler compiler, string name) {
      if (compiler.enclosing == null) {
        return null;
      }
      var enclosing = compiler.enclosing;
      var local = FindLocal(enclosing, name);
      if (local != null) {
        local.IsCaptured = true;
        return AddUpvalue(compiler, local.Sp, true, name, local.IsConst, enclosing.fiberName);
      }
      var pair = ResolveUpvalue(enclosing, name);
      if (pair != null) {
        return AddUpvalue(compiler, pair.Value.Item2, false, name, pair.Value.Item1.IsConst, enclosing.fiberName);
      }
      return null;
    }

    private (Upvalue, int) AddUpvalue(Compiler compiler, int sp, bool isLocal, string name, bool isConst, string fiberName) {
      for (var i = 0; i < compiler.upvalues.Count; i++) {
        var existingUpvalue = compiler.upvalues[i];
        if (existingUpvalue.Sp == sp && existingUpvalue.IsLocal == isLocal && existingUpvalue.FiberName == fiberName) {
          return (existingUpvalue, i);
        }
      }
      var upvalue = new Upvalue(sp, isLocal, name, isConst, fiberName);
      compiler.upvalues.Add(upvalue);
      return (upvalue, compiler.upvalues.Count - 1);
    }

    private OpCode OpCodeForCompoundAssignment(TokenType type) => type switch {
      TokenType.PLUS_EQUAL => OpCode.ADD,
      TokenType.MINUS_EQUAL => OpCode.SUBTRACT,
      TokenType.STAR_EQUAL => OpCode.MULTIPLY,
      TokenType.SLASH_EQUAL => OpCode.DIVIDE,
      TokenType.SLASH_SLASH_EQUAL => OpCode.DIVIDE_INT,
      TokenType.MOD_EQUAL => OpCode.MOD,
      _ => throw new Exception("WTF"),
    };

    private void CheckIfUnidentified() {
      if (lastChunk?.OpCode == OpCode.CONST_ID) {
        throw Error(Previous, $"Unable to resolve identifier: {lastChunk.Data[1]}");
      }
    }

    private string NextLambdaName() {
      return $"__lambda_{NumberOfEnclosingCompilers}_{lambdaCounter++}__";
    }

    private string NextImplicitVarName(string opener) {
      return $"__{opener}_{NumberOfEnclosingCompilers}_{implicitVarCounter++}__";
    }

    private ISet<string> ScanForImplicitArgs(Token opener, bool isExpr) {
      var args = new SortedSet<string>();
      var curr = current;
      List<Token> bodyTokens;
      if (isExpr) {
        bodyTokens = ScanNextExpr();
      } else {
        current--; // Go back to {
        bodyTokens = ConsumeNextBlock(false);
      }
      current = curr;
      foreach (var token in bodyTokens) {
        if (token.Type == TokenType.IDENTIFIER) {
          var argName = token.Lexeme;
          if (IMPLICIT_ARG.IsMatch(argName)) {
            args.Add(argName);
          }
        }
      }
      for (var i = 0; i < args.Count; i++) {
        var expected = $"${i}";
        if (args.ElementAt(i) != expected) {
          throw Error(opener, $"Invalid implicit arg order, found {args.ElementAt(i)} instead of {expected}");
        }
      }
      return args;
    }

    private bool ValidateImplicitThis(ClassCompiler compiler, string name) {
      if (compiler == null) {
        return false;
      }
      if (compiler.DeclaredFields.Contains(name)) {
        return true;
      }
      foreach (var superclass in compiler.Superclasses) {
        if (ValidateImplicitThis(FindCompiledClass(superclass), name)) {
          return true;
        }
      }
      return false;
    }

    private ClassCompiler FindCompiledClass(string name) {
      Compiler compiler = this;
      while (compiler != null) {
        var classCompiler = compiledClasses.GetValue(name);
        if (classCompiler != null) {
          return classCompiler;
        }
        compiler = compiler.enclosing;
      }
      return null;
    }

    private void UpdateCurrentCodePoint(Token token) {
      if (DebugMode) {
        currentCodePoint = new CodePointer(token);
      }
    }

    private void UpdateDebugInfo(Token token) {
      if (DebugMode) {
        UpdateCurrentCodePoint(token);
        if (debugInfoLines.ContainsKey(currentCodePoint)) {
          return;
        }
        debugInfoLines[currentCodePoint] = byteCode.Count;
        if (token.HasBreakpoint) {
          debugInfoBreakpoints.Add(byteCode.Count);
        }
      }
    }

    public class Local {
      public string Name { get; }
      public int Sp { get; }
      public int Depth { get; }
      public bool IsCaptured { get; set; }
      public bool IsConst { get; set; }

      public Local(string name, int sp, int depth, bool isCaptured) {
        Name = name;
        Sp = sp;
        Depth = depth;
        IsCaptured = isCaptured;
        IsConst = (name == Constants.THIS || CONST_IDENTIFIER.IsMatch(name));
      }
    }

    class Upvalue {
      public int Sp { get; }
      public bool IsLocal { get; }
      public string Name { get; }
      public bool IsConst { get; }
      public string FiberName { get; }

      public Upvalue(int sp, bool isLocal, string name, bool isConst, string fiberName) {
        Sp = sp;
        IsLocal = isLocal;
        Name = name;
        IsConst = isConst;
        FiberName = fiberName;
      }
    }

    class ActiveLoop {
      public int Start { get; }
      public int Depth { get; }

      private readonly List<Chunk> breaks = new();
      public List<Chunk> Breaks => breaks;

      public ActiveLoop(int start, int depth) {
        Start = start;
        Depth = depth;
      }
    }

    class Chunk {
      public OpCode OpCode { get; }
      public int Size { get; set; }
      public object[] Data { get; }
      public int Pos { get; set; }

      public Chunk(OpCode opCode, int size, params object[] data) {
        OpCode = opCode;
        Size = size;
        Data = data;
      }

      public override string ToString() {
        return $"{string.Format("{0,:D5}", Pos)}: {OpCode}, size: {Size}, data: {string.Join(", ", Data)}";
      }
    }

    class ClassCompiler {
      public string Name { get; }
      public ClassKind Kind { get; }
      public ClassCompiler Enclosing { get; set; }

      private readonly List<string> superclasses = new();
      public List<string> Superclasses => superclasses;

      private readonly List<Token> initAdditionalTokens = new();
      public List<Token> InitAdditionalTokens => initAdditionalTokens;

      private readonly HashSet<string> declaredFields = new();
      public HashSet<string> DeclaredFields => declaredFields;

      public ClassCompiler(string name, ClassKind kind, ClassCompiler enclosing) {
        Name = name;
        Kind = kind;
        Enclosing = enclosing;
      }
    }

    class ArgScanResult {
      public List<string> Args { get; }
      public List<Token> PrependedTokens { get; }
      public int OptionalParamsStart { get; }
      public List<object> DefaultValues { get; }

      public ArgScanResult(List<string> args, List<Token> prependedTokens,
              int optionalParamsStart, List<Object> defaultValues) {
        Args = args;
        PrependedTokens = prependedTokens;
        OptionalParamsStart = optionalParamsStart;
        DefaultValues = defaultValues;
      }
    }

    class RolledBackChunk {
      public Chunk Chunk { get; }
      public List<byte> Data { get; }

      public RolledBackChunk(Chunk chunk, List<byte> data) {
        Chunk = chunk;
        Data = data;
      }
    }

    class CompilerError : Exception {
      public CompilerError(string message) : base(message) { }
    }

    private class StatementDeepDown : Exception { }

    enum FunctionKind {
      SCRIPT, FUNCTION, METHOD, FIBER, INIT, LAMBDA
    }
  }
}