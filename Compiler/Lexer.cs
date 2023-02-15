using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NusantaraScript {
  public class Lexer {
    private static readonly char[] WHITESPACES = { ' ', '\r', '\t' };
    private static readonly Dictionary<string, TokenType?> KEYWORDS = new() {
      { "break", TokenType.BREAK },
      { "continue", TokenType.CONTINUE },
      { "else", TokenType.ELSE },
      { "this", TokenType.THIS },
      { "super", TokenType.SUPER },
      { "false", TokenType.FALSE },
      { "fn", TokenType.FN },
      { "for", TokenType.FOR },
      { "if", TokenType.IF },
      { "is", TokenType.IS },
      { "null", TokenType.NULL },
      { "return", TokenType.RETURN },
      { "true", TokenType.TRUE },
      { "while", TokenType.WHILE },
      { "when", TokenType.WHEN },
      { "in", TokenType.IN },
      { "throw", TokenType.THROW },
      { "import", TokenType.IMPORT },
      { "class", TokenType.CLASS },
      { "effect", TokenType.EFFECT },
      { "stance", TokenType.STANCE },
      { "scenario", TokenType.SCENARIO },
      { "ai", TokenType.AI },
    };

    private readonly string fileName;
    private readonly string source;
    private readonly bool isDebug;

    private readonly List<Token> tokens = new();
    private readonly List<string> imports = new();
    private int start = 0;
    private int current = 0;
    private int line = 1;
    private int stringInterpolationParentheses = 0;

    public Lexer(string fileName, string source, bool isDebug) {
      this.fileName = fileName;
      this.source = source;
      this.isDebug = isDebug;
    }

    public LexerOutput ScanTokens(bool addEof) {
      while (!IsAtEnd) {
        start = current;
        ScanToken();
      }
      if (addEof) {
        tokens.Add(new Token(TokenType.EOF, "", null, line, fileName));
      }
      return new LexerOutput(tokens.ToArray(), imports.ToArray());
    }

    private void ScanToken() {
      char c = Advance();
      switch (c) {
        case '(':
          AddToken(TokenType.LEFT_PAREN);
          if (stringInterpolationParentheses > 0) {
            stringInterpolationParentheses++;
          }
          break;
        case ')':
          AddToken(TokenType.RIGHT_PAREN);
          if (stringInterpolationParentheses > 0) {
            stringInterpolationParentheses--;
            if (stringInterpolationParentheses == 0) {
              ContinueStringInterpolation();
            }
          }
          break;
        case '[': AddToken(TokenType.LEFT_BRACKET); break;
        case ']': AddToken(TokenType.RIGHT_BRACKET); break;
        case '{': AddToken(TokenType.LEFT_BRACE); break;
        case '}': AddToken(TokenType.RIGHT_BRACE); break;
        case ',': AddToken(TokenType.COMMA); break;
        case '.':
          if (Match(',')) {
            AddToken(TokenType.DOT_COMMA);
          } else if (Match('.')) {
            if (Match('.')) {
              AddToken(TokenType.DOT_DOT_DOT);
            } else {
              AddToken(TokenType.DOT_DOT);
            }
          } else {
            AddToken(TokenType.DOT);
          }
          break;
        case ':':
          if (Match(':')) {
            AddToken(TokenType.COLON_COLON);
          } else {
            AddToken(TokenType.COLON);
          }
          break;
        case '@':
          tokens.Add(new Token(TokenType.THIS, Constants.THIS, null, line, fileName));
          AddToken(TokenType.DOT);
          break;
        case '=':
          if (Match('=')) {
            AddToken(TokenType.EQUAL_EQUAL);
          } else if (Match('>')) {
            AddToken(TokenType.EQUAL_GREATER);
          } else {
            AddToken(TokenType.EQUAL);
          }
          break;
        case '<':
          if (Match('=')) {
            AddToken(TokenType.LESS_EQUAL);
          } else {
            AddToken(TokenType.LESS);
          }
          break;
        case '!':
          if (Match('=')) {
            AddToken(TokenType.BANG_EQUAL);
          } else if (MatchAll("is ")) {
            AddToken(TokenType.NOT_IS);
          } else if (MatchAll("in ")) {
            AddToken(TokenType.NOT_IN);
          } else {
            AddToken(TokenType.BANG);
          }
          break;
        case '>': AddToken(Match('=') ? TokenType.GREATER_EQUAL : TokenType.GREATER); break;
        case '+': AddToken(Match('=') ? TokenType.PLUS_EQUAL : TokenType.PLUS); break;
        case '-': AddToken(Match('=') ? TokenType.MINUS_EQUAL : TokenType.MINUS); break;
        case '/':
          if (Match('/')) {
            if (Match('=')) {
              AddToken(TokenType.SLASH_SLASH_EQUAL);
            } else {
              AddToken(TokenType.SLASH_SLASH);
            }
          } else if (Match('=')) {
            AddToken(TokenType.SLASH_EQUAL);
          } else {
            AddToken(TokenType.SLASH);
          }
          break;
        case '*':
          if (Match('*')) {
            AddToken(TokenType.STAR_STAR);
          } else if (Match('=')) {
            AddToken(TokenType.STAR_EQUAL);
          } else {
            AddToken(TokenType.STAR);
          }
          break;
        case '%':
          if (Match('%')) {
            AddToken(TokenType.MOD_MOD);
          } else if (Match('=')) {
            AddToken(TokenType.MOD_EQUAL);
          } else {
            AddToken(TokenType.MOD);
          }
          break;
        case '$': Identifier(); break;
        case '#': Comment(); break;
        case '\\':
          if (Match('\n')) {
            line++;
          }
          break;
        case '\n':
          AddToken(TokenType.NEWLINE);
          line++;
          break;
        case ';': AddToken(TokenType.NEWLINE); break;
        default:
          if (MatchAll("??", true)) {
            AddToken(TokenType.QUESTION_QUESTION);
          } else if (MatchAll("?!", true)) {
            AddToken(TokenType.QUESTION_BANG);
          } else if (MatchAll("?.,", true)) {
            AddToken(TokenType.QUESTION_DOT_COMMA);
          } else if (MatchAll("?.", true)) {
            AddToken(TokenType.QUESTION_DOT);
          } else if (MatchAll("&&", true)) {
            AddToken(TokenType.AND_AND);
          } else if (MatchAll("||", true)) {
            AddToken(TokenType.OR_OR);
          } else if (IsStringDelim(c)) {
            StringValue(true);
          } else if (IsDigit(c)) {
            Number();
          } else if (IsAlpha(c)) {
            Identifier();
          } else if (!WHITESPACES.Contains(c)) {
            throw Error("Unexpected character.");
          }
          break;
      }
    }

    private void Comment() {
      if (Match('*')) { // Multi line
        while (!MatchAll("*#")) {
          if (Peek == '\n') {
            line++;
          }
          Advance();
        }
      } else { // Single line
               // A comment goes until the end of the line.
        while (Peek != '\n' && !IsAtEnd) {
          Advance();
        }
        if (isDebug) { // look for breakpoint marks in comments
          var comment = source.Substring(start + 1, current - start - 1);
          if (comment.Trim().StartsWith(Debugger.BREAKPOINT_LEXEME)) {
            for (var i = tokens.Count - 1; i >= 0; i--) {
              var token = tokens[i];
              if (token.Line == line) {
                token.HasBreakpoint = true;
              } else {
                break;
              }
            }
          }
        }
      }
    }

    private void Identifier() {
      while (IsAlphaNumeric(Peek)) {
        Advance();
      }

      // See if the identifier is a reserved word.
      var text = source.Substring(start, current - start);
      var type = KEYWORDS.GetValue(text);
      if (type == TokenType.IMPORT) {
        var curr = current;
        try {
          var stringOpener = MatchWhiteSpacesUntilStringOpener();
          start = current - 1;
          var importPath = StringValue(false);
          if (importPath == null) {
            throw Error("String interpolation is forbidden in imports.");
          }
          imports.Add(importPath);
          return;
        } catch (Exception) {
          current = curr;
        }
      } else if (type == null) {
        type = TokenType.IDENTIFIER;
      }
      AddToken((TokenType)type);
    }

    private void Number() {
      while (IsDigitOrUnderscore(Peek)) {
        Advance();
      }
      // Look for a fractional part.
      if (Peek == '.' && IsDigit(PeekNext)) {
        // Consume the "."
        Advance();
        while (IsDigitOrUnderscore(Peek)) {
          Advance();
        }
      }
      // Exp notation
      if (Peek == 'e' || Peek == 'E') {
        if (IsDigit(PeekNext)) {
          Advance();
        } else if (PeekNext == '+' || PeekNext == '-') {
          Advance();
          Advance();
        } else {
          throw Error("Expected a digit or + or - after E!");
        }
        while (IsDigitOrUnderscore(Peek)) {
          Advance();
        }
      }
      var numberString = source.Substring(start, current - start).Replace("_", "");
      IPrimitiveValue literal = null;
      try {
        literal = new Int(long.Parse(numberString));
      } catch (FormatException) {
        literal = new Float(double.Parse(numberString, CultureInfo.InvariantCulture));
      }
      AddToken(TokenType.NUMBER, literal);
    }

    private string StringValue(bool AddTokenIfNotInterpolation) {
      while (Peek != '"' && !IsAtEnd) {
        if (Peek == '\n') {
          line++;
        } else if (Peek == '\\') {
          var next = PeekNext;
          if (next == '"' || next == '$' || next == 'n' || next == '\\') {
            Advance();
          } else {
            throw Error("Invalid string escape char: " + next);
          }
        } else if (Peek == '$') {
          var next = PeekNext;
          if (next == '(') { // String interpolation
            var valueSoFar = EscapedString(start + 1, current);
            AddToken(TokenType.STRING, new Str(valueSoFar));
            AddToken(TokenType.PLUS);
            Advance(); // Skip the \
            Advance(); // Skip the (
            AddToken(TokenType.LEFT_PAREN);
            stringInterpolationParentheses = 1;
            return valueSoFar;
          } else if (IsAlpha(next)) {
            var valueSoFar = EscapedString(start + 1, current);
            AddToken(TokenType.STRING, new Str(valueSoFar));
            Advance();
            AddToken(TokenType.PLUS);
            start = current;
            while (IsAlphaNumeric(Peek)) {
              Advance();
            }
            SynthesizeIdentifier(source.Substring(start, current - start));
            start = current - 1; // we start scanning at -1 because string() normally assumes that it starts with a string opener - otherwise, it'd skip the first char immediately after the $ID
            ContinueStringInterpolation();
            return null;
          }
        }
        Advance();
      }

      // Unterminated string.
      if (IsAtEnd) {
        throw Error("Unterminated string.");
      }

      // The closing ".
      Advance();

      // Trim the surrounding quotes.
      var startAfterQuote = start + 1;
      var endBeforeQuote = current - 1;
      var value = (startAfterQuote >= endBeforeQuote) ? "" : EscapedString(startAfterQuote, endBeforeQuote);
      if (AddTokenIfNotInterpolation) {
        AddToken(TokenType.STRING, new Str(value));
      }
      return value;
    }

    private string EscapedString(int start, int stop) {
      var backslashBackslashNReplacement = "\\\\r";
      return source.Substring(start, stop - start)
          .Replace("\\\\n", backslashBackslashNReplacement)
          .Replace("\\n", "\n")
          .Replace(backslashBackslashNReplacement, "\\n")
          .Replace("\\t", "\t")
          .Replace("\\\"", "\"")
          .Replace("\\\\", "\\");
    }

    private string KeywordString(TokenType type) {
      foreach (var s in KEYWORDS.Keys) {
        if (KEYWORDS.GetValue(s) == type) {
          return s;
        }
      }
      return null;
    }

    private bool MatchPeek(TokenType type) {
      var keyword = KeywordString(type);
      if (keyword == null) {
        return false;
      }
      var len = keyword.Length;
      var end = current + len + 1;
      if (end < source.Length && source.Substring(current + 1, len) == keyword) {
        current = end;
        return true;
      }
      return false;
    }

    private bool Match(char expected) {
      if (IsAtEnd) {
        return false;
      }
      if (source[current] != expected) {
        return false;
      }
      current++;
      return true;
    }

    private bool MatchAll(string expected, bool includeCurrent = false) {
      var start = includeCurrent ? (current - 1) : current;
      var end = start + expected.Length;
      if (end >= source.Length) {
        return false;
      }
      if (source.Substring(start, expected.Length) != expected) {
        return false;
      }
      current = end;
      return true;
    }

    private bool MatchKeywords(TokenType type) {
      foreach (var keyword in KEYWORDS) {
        if (keyword.Value == type) {
          return MatchAll(keyword.Key);
        }
      }
      return false;
    }

    private void MatchAllWhitespaces() {
      while (WHITESPACES.Contains(Advance())) { }
      current--; // Skip back to the first char that's not a whitespace
    }

    private char MatchWhiteSpacesUntilStringOpener() {
      MatchAllWhitespaces();
      var c = Advance();
      if (IsStringDelim(c)) {
        return c;
      } else {
        throw Error("Expected a string!");
      }
    }

    private char Peek => IsAtEnd ? '\u0000' : source[current];

    private char PeekNext => (current + 1 >= source.Length) ? '\u0000' : source[current + 1];

    private bool IsAlpha(char c) => (Char.ToLower(c) >= 'a' && Char.ToLower(c) <= 'z') || c == '_';

    private bool IsDigit(char c) => (Char.ToLower(c) >= '0' && Char.ToLower(c) <= '9');

    private bool IsAlphaNumeric(char c) => IsAlpha(c) || IsDigit(c);

    private bool IsDigitOrUnderscore(char c) => IsDigit(c) || c == '_';

    private bool IsStringDelim(char c) => c == '"';

    private bool IsAtEnd => current >= source.Length;

    private char Advance() {
      current++;
      return source[current - 1];
    }

    private void AddToken(TokenType type, IPrimitiveValue literal = null) {
      var text = source.Substring(start, current - start);
      tokens.Add(new Token(type, text, literal, line, fileName));
    }

    private void SynthesizeIdentifier(string value) {
      tokens.Add(new Token(TokenType.IDENTIFIER, value, null, line, fileName));
    }

    private void SynthesizeToken(TokenType type) {
      tokens.Add(new Token(type, "", null, line, fileName)); // should be type.toCode()
    }

    private void ContinueStringInterpolation() {
      AddToken(TokenType.PLUS);
      var str = StringValue(true);
      if (str?.Length == 0) { // the interpolation was the last thing in the string, no need for the PLUS and the empty string to take up token space
        RollBackToken();
        RollBackToken();
      }
    }

    private void RollBackToken() {
      tokens.RemoveAt(tokens.Count - 1);
    }

    private Exception Error(string message) {
      return new LexerError($"[\"{fileName}\" line {line}] Error: {message}");
    }
  }

  public class LexerError : Exception {
    public LexerError(string message) : base(message) { }
  }

  public class LexerOutput {
    public Token[] Tokens { get; }
    public string[] Imports { get; }

    public LexerOutput(Token[] tokens, string[] imports) {
      Tokens = tokens;
      Imports = imports;
    }
  }
}