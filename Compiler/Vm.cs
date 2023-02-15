using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using MiscUtil.IO;

namespace NusantaraScript {
  public class Vm {
    public const int INITIAL_STACK_SIZE = 256;
    private const int STACK_GROWTH_FACTOR = 4;
    public const int MAX_FRAMES = 1024;

    public Fiber Fiber { get; set; }
    private int liveInstanceStartingFiberSp = 0; // first index after the immutable part of the stack in a live VM instance
    private readonly Dictionary<string, UpValue> openUpvalues = new();
    private readonly Stack<int> nonFinalizedClasses = new();
    private readonly Dictionary<string, SClass> declaredClasses = new() {
            { Constants.CLASS_STR, CoreClasses.CLASS_STR },
            { Constants.CLASS_INT, CoreClasses.CLASS_INT },
            { Constants.CLASS_FLOAT, CoreClasses.CLASS_FLOAT },
            { Constants.CLASS_LIST, CoreClasses.CLASS_LIST }
        };
    public Debugger Debugger { get; private set; }

    public EndianBinaryReader Buffer => Fiber.frame.Buffer;
    public Function CurrentFunction => Fiber.frame.Closure.Function;
    private SClass CurrentNonFinalizedClass => (SClass)Fiber.stack[nonFinalizedClasses.Peek()];
    private object This => Fiber.stack[Fiber.frame.Sp];

    private OpCode NextCode => OpCodeExtensions.From(NextByte);
    private byte NextByte => Buffer.ReadByte();
    public bool NextBool => NextByte == 1;
    private int NextInt => Buffer.ReadInt32();
    private long NextLong => Buffer.ReadInt64();
    private double NextDouble => Buffer.ReadDouble();
    private string NextString => (string)CurrentFunction.Constants[NextInt];

    private readonly Mutex mutex = new();
    public Action<Exception> errorLogger;

    public void Interpret(Fiber input, Debugger debugger, Action<Exception> errorLogger, params IFielded[] initialObjects) {
      Debugger = debugger;
      this.errorLogger = errorLogger;
      Fiber = input;
      Push(input);
      foreach (var initialObject in initialObjects) {
        Push(initialObject);
      }
      try {
        Await(true, initialObjects.Length);
      } catch (Exception e) {
        // Silently abort the program
        errorLogger?.Invoke(e);
      }
    }

    private void Await(bool isRoot, int argCount) {
      var caller = isRoot ? null : Fiber;
      Fiber = (Fiber)Peek(argCount);
      Fiber.caller = caller;
      if (isRoot || Fiber.state == Fiber.State.NEW) {
        Fiber.state = Fiber.State.STARTED;
        if (!isRoot) {
          Push(Fiber.Closure);
        }
        MoveArgsFromCaller(argCount);
        CallClosure(Fiber.Closure, argCount);
        Fiber.SetSpToEndOfStack(); // allows for live interaction with the VM
        liveInstanceStartingFiberSp = Fiber.sp;

      } else {
        MoveArgsFromCaller(argCount);
        Run(0);
      }
    }

    private void MoveArgsFromCaller(int argCount) {
      if (Fiber.caller != null) {
        for (var i = 0; i < argCount; i++) {
          // copy args to the top (+1 for closure) of the fiber stack
          Fiber.stack[i + 1] = Fiber.caller.stack[Fiber.caller.sp - argCount - 1];
        }
        Fiber.caller.sp -= argCount; // remove args from caller stack
                                     // if this is the first invocation and the the fiber stack is empty, bump its sp
        if (Fiber.sp < argCount + 1) {
          Fiber.sp = argCount + 1;
        }
      }
    }

    private void Run(int breakAtFp, Func<bool> predicate = null) {
      bool run = true;
      while (run && predicate?.Invoke() != false) {
        Debugger?.TriggerBreakpoint();
        var code = NextCode;
        switch (code) {
          case OpCode.UNKNOWN:
            throw new Exception("Unknown opcode");
          case OpCode.TRUE:
            Push(true);
            break;
          case OpCode.FALSE:
            Push(false);
            break;
          case OpCode.CONST_INT:
            Push(NextLong);
            break;
          case OpCode.CONST_FLOAT:
            Push(NextDouble);
            break;
          case OpCode.CONST_ID:
            Push(NextString);
            break;
          case OpCode.CONST:
            PushConst();
            break;
          case OpCode.NIL:
            Push(Nil.INSTANCE);
            break;
          case OpCode.POP:
            Fiber.sp--;
            break;
          case OpCode.POP_UNDER: {
              var value = Pop();
              Fiber.sp -= NextInt;
              Push(value);
            }
            break;
          case OpCode.DUPLICATE:
            Push(Peek());
            break;
          case OpCode.SET_LOCAL:
            SetVar();
            break;
          case OpCode.GET_LOCAL:
            GetVar(NextInt);
            break;
          case OpCode.SET_UPVALUE:
            Fiber.frame.Closure.Upvalues[NextInt].Location = Fiber.sp - 1; // -1 because we need to point at an actual slot
            break;
          case OpCode.GET_UPVALUE:
            Push(ReadUpvalue(Fiber.frame.Closure.Upvalues[NextInt]));
            break;
          case OpCode.SET_PROP:
            SetProp();
            break;
          case OpCode.GET_PROP:
            GetProp();
            break;
          case OpCode.UPDATE_PROP:
            UpdateProp();
            break;
          case OpCode.INVERT:
            Invert();
            break;
          case OpCode.NEGATE:
            Negate();
            break;
          case OpCode.ADD:
            Add();
            break;
          case OpCode.SUBTRACT:
          case OpCode.MULTIPLY:
          case OpCode.DIVIDE:
          case OpCode.DIVIDE_INT:
          case OpCode.MOD:
          case OpCode.LE:
          case OpCode.LT:
          case OpCode.GT:
          case OpCode.GE:
            BinaryOpOnStack(code);
            break;
          case OpCode.EQ:
            CheckEquality(code);
            break;
          case OpCode.IS:
            CheckIs();
            break;
          case OpCode.HAS: {
              var b = Fiber.stack[Fiber.sp - 1];
              Fiber.stack[Fiber.sp - 1] = Fiber.stack[Fiber.sp - 2];
              Fiber.stack[Fiber.sp - 2] = b;
              Invoke(Constants.HAS, 1, true, false);
              var result = Pop();
              if (result == Nil.INSTANCE) {
                Push(false);
              } else {
                Push(result);
              }
            }
            break;
          case OpCode.JUMP:
            Buffer.Seek(NextInt);
            break;
          case OpCode.JUMP_IF_FALSE:
            JumpIf(o => IsFalse(o));
            break;
          case OpCode.JUMP_IF_NIL:
            JumpIf(o => o == Nil.INSTANCE);
            break;
          case OpCode.JUMP_IF_THROWN:
            JumpIf(o => o is Thrown);
            break;
          case OpCode.CALL: {
              var argCount = NextInt;
              Call(Peek(argCount), argCount);
            }
            break;
          case OpCode.INVOKE: {
              var name = NextString;
              var argCount = NextInt;
              var nullSafeCall = NextBool;
              var cascadeCall = NextBool;
              Invoke(name, argCount, !nullSafeCall, cascadeCall);
            }
            break;
          case OpCode.CLOSURE:
            Closure();
            break;
          case OpCode.CLOSE_UPVALUE:
            CloseUpvalue();
            break;
          case OpCode.RETURN:
          case OpCode.THROW:
            if (ReturnOrThrow(breakAtFp, code == OpCode.THROW)) {
              run = false;
            }
            break;
          case OpCode.CLASS:
            DeclareClass(ClassKindExtensions.From(NextByte), NextString);
            break;
          case OpCode.INHERIT:
            Inherit();
            break;
          case OpCode.METHOD:
          case OpCode.FIELD:
            DefineMethodOrField(NextString);
            break;
          case OpCode.CLASS_DECLR_DONE:
            nonFinalizedClasses.Pop();
            break;
          case OpCode.SUPER: {
              var superclass = NextString;
              var klass = (This as Instance)?.Class ?? (This as SClass);
              var superklass = klass.GetSuperclass(superclass);
              if (superklass != null) {
                Push(superklass);
              } else {
                throw RuntimeError($"Class {klass.Name} doesn't inherit from {superclass}!");
              }
            }
            break;
          case OpCode.GET_SUPER:
            GetSuper();
            break;
          case OpCode.SUPER_INVOKE:
            InvokeSuper(NextString, NextInt, !NextBool, NextBool);
            break;
          case OpCode.OBJECT:
            ObjectLiteral(NextInt);
            break;
          case OpCode.LIST:
            ListLiteral(NextInt);
            break;
          case OpCode.RANGE_TO:
            RangeTo();
            break;
          case OpCode.UNBOX_THROWN:
            UnboxThrown();
            break;
        }
      }
    }

    private void RunLocal() {
      Run(Fiber.Fp - 1);
    }

    private void ResizeStackIfNecessary() {
      if (Fiber.sp == Fiber.stackSize) {
        Gc();
        Fiber.stackSize *= STACK_GROWTH_FACTOR;
        Array.Resize(ref Fiber.stack, Fiber.stackSize);
      }
    }

    private void Push(object o) {
      ResizeStackIfNecessary();
      Fiber.stack[Fiber.sp] = o;
      Fiber.sp++;
    }

    private object Pop() {
      Fiber.sp--;
      return Fiber.stack[Fiber.sp];
    }

    private object Peek(int offset = 0) {
      return Fiber.stack[Fiber.sp - offset - 1];
    }

    private void Set(int offset, object value) {
      Fiber.stack[Fiber.sp - offset - 1] = value;
    }

    private Dictionary<string, object> GetFieldsOrThrow(object it, bool throwRuntimeError) {
      if (it is IFielded f) {
        return f.Fields;
      } else if (it is Dictionary<string, object> dict) {
        return dict;
      } else if (throwRuntimeError) {
        throw RuntimeError("Only instances have fields!");
      } else {
        throw new Exception(); // silent exception
      }
    }

    private SClass GetSClass(object it) {
      if (it is Instance i) {
        return i.Class;
      } else if (it is SClass c) {
        return c;
      } else {
        throw RuntimeError("Unable to get SClass for " + it);
      }
    }

    private void BindMethod(object receiver, SClass klass, object prop, string name) {
      var bound = GetBoundMethod(receiver, klass, prop, name);
      if (bound != null) {
        Fiber.sp--;
        Push(bound);
      }
    }

    private BoundCallable GetBoundMethod(object receiver, SClass klass, object prop, string name) {
      if (prop is INativeFunction f) {
        return new BoundNativeMethod(receiver, f);
      } else {
        Closure method;
        if (prop is Closure c) {
          method = c;
        } else {
          var field = klass?.Fields?.GetValue(name);
          if (field is Closure fc) {
            method = fc;
          } else {
            return null;
          }
        }
        return new BoundMethod(receiver, method);
      }
    }

    private void Gc() {
      for (var i = Fiber.sp; i < Fiber.stackSize; i++) {
        Fiber.stack[i] = null;
      }
      System.GC.Collect();
    }

    private void PushConst() {
      Push(CurrentFunction.Constants[NextInt]);
    }

    private void SetVar() {
      Fiber.stack[Fiber.frame.Sp + NextInt] = Pop();
    }

    private void GetVar(int sp) {
      Push(Fiber.stack[Fiber.frame.Sp + sp]);
    }

    private void SetProp() {
      var value = Pop();
      var prop = Pop();
      var obj = Pop();
      if (obj is SClass) {
        throw RuntimeError("Can't set property of a class!");
      }
      if (obj is ListInstance list && prop is long l) {
        list[Convert.ToInt32(l)] = value;
      } else {
        GetFieldsOrThrow(obj, true)[Stringify(prop)] = value;
      }
    }

    private void GetProp() {
      var key = Pop();
      var obj = BoxIfNotInstance(0);
      Fiber.sp--;
      if (key is long l) {
        if (obj is ListInstance list) {
          Push(list[Convert.ToInt32(l)]);
        } else {
          throw RuntimeError("Attempting to use index on a non-List object!");
        }
      } else {
        var name = Stringify(key);
        object prop;
        if (obj == Nil.INSTANCE) {
          prop = null;
        } else if (obj is Instance inst) {
          prop = inst.Fields.GetValue(name) ?? inst.Class.Fields.GetValue(name);
        } else if (obj is IFielded) {
          prop = obj.Fields.GetValue(name);
        } else {
          throw RuntimeError("Only instances can have fields!");
        }
        Push(prop ?? Nil.INSTANCE);
        if (prop != null) {
          BindMethod(obj, GetSClass(obj), prop, name);
        }
      }
    }

    /**
    * The algorithm is as follows:
    * 1. At the beginning, the top contains the right-hand side value and the prepared, unexecuted getter. Pop the value and keep references to object and prop for setting.
    * 2. getProp() to convert the prop and obj to a single value.
    * 3. Add the value again to the top, then invoke a single run with the next code, which the the op data of UPDATE_PROP. It'll pop the value and the prop and combine them into a single value.
    * 4. Pop this combined value and prepare for a setter - push the object, the prop and finally the combined value again.
    * 5. Invoke setProp() to store it where it belongs.
    */
    private void UpdateProp() {
      var value = Pop();
      var prop = Peek();
      var obj = Peek(1);
      GetProp();
      Push(value);
      var nextBufferPos = Buffer.Position + 1;
      Run(Fiber.Fp, () => Buffer.Position < nextBufferPos);
      value = Pop();
      Push(obj);
      Push(prop);
      Push(value);
      SetProp();
    }

    private void GetSuper() {
      var name = Stringify(Pop());
      var superclass = Pop() as SClass;
      var prop = superclass?.Fields?.GetValue(name);
      Push(prop ?? Nil.INSTANCE);
      if (This is Instance i) {
        BindMethod(i, superclass, prop, name);
      }
    }

    private object ReadUpvalue(UpValue upvalue) {
      if (upvalue.Closed) {
        return upvalue.Location;
      } else {
        return ReadUpvalueFromStack(upvalue);
      }
    }

    private object ReadUpvalueFromStack(UpValue upvalue) {
      var currentFiber = Fiber;
      while (currentFiber != null && currentFiber.Name != upvalue.FiberName) {
        currentFiber = currentFiber.caller;
      }
      if (currentFiber != null) {
        if (upvalue.Sp < currentFiber.sp) {
          var value = currentFiber.stack[upvalue.Sp];
          if (value != null) {
            return value;
          }
        }
      }
      throw RuntimeError("Unable to read upvalue from stack.");
    }

    private UpValue CaptureUpvalue(int sp, string fiberName) {
      UpValue prevUpvalue = null;
      var upvalue = openUpvalues.GetValue(fiberName);
      while (upvalue != null && upvalue.Sp > sp) {
        prevUpvalue = upvalue;
        upvalue = upvalue.Next;
      }
      if (upvalue != null && upvalue.Sp == sp) {
        return upvalue;
      }
      var createdUpvalue = new UpValue(sp, fiberName, false, upvalue);
      if (prevUpvalue == null) {
        openUpvalues[fiberName] = createdUpvalue;
      } else {
        prevUpvalue.Next = createdUpvalue;
      }
      return createdUpvalue;
    }

    private void CloseUpvalues(int last, string fiberName) {
      var openUpvalue = openUpvalues.GetValue(fiberName);
      while (openUpvalue != null && openUpvalue.Sp >= last) {
        var upvalue = openUpvalue;
        upvalue.Closed = true;
        upvalue.Location = ReadUpvalueFromStack(upvalue);
        openUpvalue = upvalue.Next;
      }
      if (openUpvalue != null) {
        openUpvalues[fiberName] = openUpvalue;
      } else {
        openUpvalues.Remove(fiberName);
      }
    }

    private void CloseUpvalue() {
      CloseUpvalues(Fiber.sp - 1, Fiber.Name);
      Fiber.sp--;
    }

    private bool IsFalse(object o) {
      if (o is bool b) {
        return !b;
      }
      return true;
    }

    internal bool IsTrue(object o) => !IsFalse(o);

    private void Invert() {
      var a = Unbox(Pop());
      Push(IsFalse(a));
    }

    private void Negate() {
      var a = Unbox(Pop());
      if (a is long l) {
        Push(-l);
      } else if (a is double d) {
        Push(-d);
      } else {
        throw RuntimeError("Trying to negate a non-number: " + a);
      }
    }

    private void Add() {
      var b = Unbox(Pop());
      var a = Unbox(Pop());
      if (a is string sa) {
        Push(sa + Stringify(b));
      } else if (b is string sb) {
        Push(Stringify(a) + sb);
      } else {
        Push(BinaryOp(OpCode.ADD, a, b));
      }
    }

    private void BinaryOpOnStack(OpCode opCode) {
      var b = Pop();
      var a = Pop();
      Push(BinaryOp(opCode, a, b));
    }

    private object BinaryOp(OpCode opCode, object a, object b) {
      if (a == Nil.INSTANCE || b == Nil.INSTANCE) {
        return Nil.INSTANCE;
      }
      var realA = Unbox(a);
      var realB = Unbox(b);
      if (realA is long la && realB is long lb) {
        return BinaryOpTwoLongs(opCode, la, lb);
      } else {
        return BinaryOpTwoDoubles(opCode, Convert.ToDouble(realA, CultureInfo.InvariantCulture), Convert.ToDouble(realB, CultureInfo.InvariantCulture));
      }
    }

    private object BinaryOpTwoLongs(OpCode opCode, long a, long b) {
      return opCode switch {
        OpCode.ADD => a + b,
        OpCode.SUBTRACT => a - b,
        OpCode.MULTIPLY => a * b,
        OpCode.DIVIDE => IntIfPossible(a * 1.0 / b),
        OpCode.DIVIDE_INT => a / b,
        OpCode.MOD => a % b,
        OpCode.LT => a < b,
        OpCode.LE => a <= b,
        OpCode.GE => a >= b,
        OpCode.GT => a > b,
        _ => throw RuntimeError("WTF"),
      };
    }

    private object BinaryOpTwoDoubles(OpCode opCode, double a, double b) {
      return opCode switch {
        OpCode.ADD => IntIfPossible(a + b),
        OpCode.SUBTRACT => IntIfPossible(a - b),
        OpCode.MULTIPLY => IntIfPossible(a * b),
        OpCode.DIVIDE or OpCode.DIVIDE_INT => IntIfPossible(a / b),
        OpCode.MOD => IntIfPossible(a % b),
        OpCode.LT => a < b,
        OpCode.LE => a <= b,
        OpCode.GE => a >= b,
        OpCode.GT => a > b,
        _ => throw RuntimeError("WTF"),
      };
    }

    private object IntIfPossible(double d) {
      var rounded = Math.Round(d);
      return (rounded == d) ? (long)rounded : d;
    }

    private void CheckEquality(OpCode opCode) {
      var b = Pop();
      var a = Pop();
      var r = AreEqual(a, b);
      Push((opCode == OpCode.EQ) ? r : !r);
    }

    private bool AreEqual(object a, object b) {
      if (a == b) {
        return true;
      }
      if (a == null || b == null || a == Nil.INSTANCE || b == Nil.INSTANCE) {
        return false;
      }
      if (a.Equals(b)) {
        return true;
      }
      if (a is Function) {
        if (b is Closure cb) {
          return a == cb.Function;
        } else {
          return false;
        }
      }
      if (a is Closure ca) {
        return AreEqual(ca.Function, b);
      }
      try {
        var idA = GetFieldsOrThrow(a, false).GetValue(Constants.FIELD_ID) as string;
        var idB = GetFieldsOrThrow(b, false).GetValue(Constants.FIELD_ID) as string;
        if (idA != null && idA == idB) {
          return true;
        }
      } catch { }
      return false;
    }

    private void CheckIs() {
      var b = (SClass)Pop();
      var a = BoxIfNotInstance(0);
      Fiber.sp--; // Pop a
      object r;
      if (a == Nil.INSTANCE) {
        r = false;
      } else if (a is Instance i) {
        r = i.Class.CheckIs(b);
      } else if (a is SClass) {
        r = a == b;
      } else {
        throw RuntimeError("WTF");
      }
      Push(r);
    }

    private void JumpIf(Func<object, bool> predicate) {
      var offset = NextInt;
      if (predicate(Peek())) {
        Buffer.Seek(offset);
      }
    }

    private void Closure() {
      var function = (Function)CurrentFunction.Constants[NextInt];
      var closure = new Closure(function);
      Push(closure);
      for (var i = 0; i < function.UpvalueCount; i++) {
        var isLocal = (NextByte == 1);
        var sp = NextInt;
        var fiberName = NextString;
        closure.Upvalues[i] = isLocal
            ? CaptureUpvalue(Fiber.frame.Sp + sp, fiberName)
            : Fiber.frame.Closure.Upvalues[sp];
      }
    }

    private void Call(object callee, int argCount) {
      var spRelativeToArgCount = Fiber.sp - argCount - 1;
      if (callee is Closure c) {
        Fiber.stack[spRelativeToArgCount] = This;
        CallClosure(c, argCount);
      } else if (callee is BoundMethod bm) {
        Fiber.stack[spRelativeToArgCount] = bm.Receiver;
        CallClosure(bm.Closure, argCount);
      } else if (callee is BoundNativeMethod bnm) {
        Fiber.stack[spRelativeToArgCount] = bnm.Receiver;
        CallNative(bnm.NativeFunction, argCount);
      } else if (callee is INativeFunction nf) {
        CallNative(nf, argCount);
      } else if (callee is SClass klass) {
        Fiber.stack[spRelativeToArgCount] = new Instance(this, klass);
        var init = (Closure)klass.Fields.GetValue(Constants.INIT);
        CallClosure(init, argCount);
      }
    }

    public Instance Instantiate(string className, object[] initArgs) {
      var klass = declaredClasses[className];
      var instance = new Instance(this, klass);
      var init = new BoundMethod(instance, (Closure)klass.Fields.GetValue(Constants.INIT));
      CallOnInstanceFromNative(instance, init, initArgs);
      return instance;
    }

    // Doesn't call init on instance.
    public Instance Instantiate(string className, Dictionary<string, object> fields) {
      var klass = declaredClasses[className];
      var instance = new Instance(this, klass, fields);
      return instance;
    }

    private void CallClosure(Closure closure, int argCount) {
      var f = closure.Function;
      HandleOptionalParams(f, argCount);
      if (Fiber.Fp == MAX_FRAMES) {
        throw RuntimeError("Stack overflow.");
      }
      Fiber.callFrames[Fiber.Fp] = new CallFrame(closure, Fiber.sp - f.Arity - 1);
      Fiber.Fp++;
      Debugger?.TriggerBreakpoint(true);
      RunLocal();
    }

    private int HandleOptionalParams(IOptionalParamsFunc f, int argCount) {
      var optionalsAddedCount = 0;
      if (argCount != f.Arity) {
        if (argCount < f.Arity
                && f.OptionalParamsStart != -1
                && argCount >= f.OptionalParamsStart) {
          for (var i = argCount; i < f.OptionalParamsStart + f.DefaultValues.Length; i++) {
            Push(f.DefaultValues[i - f.OptionalParamsStart]);
            optionalsAddedCount++;
          }
        } else {
          throw RuntimeError($"Expected {f.Arity} arguments but got {argCount}.");
        }
      }
      return optionalsAddedCount;
    }

    private void CallNative(INativeFunction f, int argCount) {
      var addedOptionalsCount = HandleOptionalParams(f, argCount);
      var totalArgCount = argCount + addedOptionalsCount;
      var args = new List<object>();
      for (var i = Fiber.sp - f.Arity - 1; i < Fiber.sp; i++) {
        args.Add(Fiber.stack[i]);
      }
      object result = null;
      if (f is NativeFunction nf) {
        result = nf.Func(args);
      }
      Fiber.sp -= totalArgCount + 1;
      Push(result ?? Nil.INSTANCE);
    }

    private bool Invoke(string name, int argCount, bool checkError, bool returnReceiver) {
      var receiver = BoxIfNotInstance(argCount);
      if (receiver == Nil.INSTANCE) {
        Fiber.sp -= argCount; // just remove the args, the receiver at slot fiber.sp - args - 1 is already Nil
        return true;
      }
      var prop = receiver.Fields.GetValue(name);
      var tryBindAndCall = BindAndCall(receiver, prop, argCount, returnReceiver);
      if (tryBindAndCall) {
        return true;
      } else {
        return InvokeFromClass(receiver, (receiver as Instance)?.Class, name, argCount, checkError, returnReceiver);
      }
    }

    private bool InvokeFromClass(object receiver, SClass klass, string name,
            int argCount, bool checkError, bool returnReceiver) {
      var method = klass?.Fields?.GetValue(name);
      var tryBindAndCall = BindAndCall(receiver, method, argCount, returnReceiver);
      if (tryBindAndCall) {
        return true;
      } else if (checkError) {
        throw RuntimeError("Undefined method: " + name);
      } else {
        Fiber.sp -= argCount + 1; // pop all the args + the instance
        if (returnReceiver) {
          Push(receiver);
        } else {
          Push(Nil.INSTANCE); // add this to stack because you're ignoring the undefined error
        }
        return true;
      }
    }

    private void InvokeSuper(string name, int argCount, bool checkError, bool returnReceiver) {
      var superclass = (SClass)Peek(argCount);
      Set(argCount, This); // bind the super method to self
      InvokeFromClass(This, superclass, name, argCount, checkError, returnReceiver);
    }

    private bool BindAndCall(object receiver, object prop, int argCount, bool returnReceiver) {
      if (prop != null) {
        object callee;
        if (prop is Closure c) {
          callee = new BoundMethod(receiver, c);
        } else if (prop is INativeFunction nf) {
          callee = new BoundNativeMethod(receiver, nf);
        } else {
          callee = prop;
        }
        Fiber.stack[Fiber.sp - argCount - 1] = callee;
        Call(callee, argCount);
        if (returnReceiver) {
          Pop();
          Push(receiver);
        }
        return true;
      } else {
        return false;
      }
    }

    private object CallOnInstanceFromNative(Instance instance, ICallable callee, object[] args) {
      Push(instance);
      var r = CallFromNative(callee, args);
      Pop(); // instance
      return r;
    }

    internal object CallFromNative(ICallable callee, params object[] args) {
      Push(callee);
      int argCount = 0;
      if (args != null) {
        argCount = args.Length;
        foreach (var arg in args) {
          Push(arg ?? Nil.INSTANCE);
        }
      }
      Call(callee, argCount);
      var r = Pop();
      return (r == Nil.INSTANCE) ? null : r;
    }

    public object InvokeFromNative(Instance instance, string name, bool requireDefined, params object[] args) {
      mutex.WaitOne();
      Push(instance);
      var argCount = args.Length;
      foreach (var arg in args) {
        Push(arg ?? Nil.INSTANCE);
      }
      try {
        Invoke(name, argCount, requireDefined, false);
      } catch (Exception e) {
        errorLogger?.Invoke(e);
      }
      var r = Pop();
      mutex.ReleaseMutex();
      return (r == Nil.INSTANCE) ? null : r;
    }

    private void DeclareClass(ClassKind kind, string name) {
      var klass = new SClass(name, kind);
      declaredClasses[name] = klass;
      nonFinalizedClasses.Push(Fiber.sp);
      Push(klass);
    }

    private void Inherit() {
      var superclass = (SClass)Pop();
      var subclass = (SClass)Peek();
      foreach (var kvp in superclass.Fields) {
        subclass.Fields[kvp.Key] = kvp.Value;
      }
      subclass.Superclasses[superclass.Name] = superclass;
    }

    private void DefineMethodOrField(string name) {
      var value = Pop();
      var klass = CurrentNonFinalizedClass;
      klass.Fields[name] = value;
    }

    private void ObjectLiteral(int propCount) {
      var props = new Dictionary<string, object>();
      for (var i = 0; i < propCount; i++) {
        var value = Pop();
        var key = (string)Pop();
        props[key] = value;
      }
      Push(new Instance(this, CoreClasses.CLASS_OBJECT, props));
    }

    private void ListLiteral(int propCount) {
      var items = new List<object>();
      for (var i = 0; i < propCount; i++) {
        var value = Pop();
        items.Add(value);
      }
      items.Reverse();
      Push(new ListInstance(this, items));
    }

    private void RangeTo() {
      var max = Pop();
      var min = Pop();
      if (!(min is long) || !(max is long)) {
        throw RuntimeError("Range limits must be integers!");
      }
      Push(CreateRange((long)min, (long)max));
    }

    internal Instance CreateRange(long min, long max) {
      return new Instance(this, CoreClasses.CLASS_RANGE, new Dictionary<string, object> {
                { "min", min },
                { "max", max }
            });
    }

    private void UnboxThrown() {
      var top = Pop();
      if (top is Thrown thrown) {
        Push(thrown.Value);
      } else {
        Push(Nil.INSTANCE);
      }
    }

    // return true if the program should terminate
    private bool ReturnOrThrow(int breakAtFp, bool wrapInThrown) {
      var result = Pop();
      if (wrapInThrown && !(result is Thrown)) {
        result = new Thrown(result);
      }
      var returningFrame = Fiber.frame;
      CloseUpvalues(returningFrame.Sp, Fiber.Name);
      var numberOfPops = NextInt;
      for (var i = 0; i < numberOfPops; i++) {
        var code = NextCode;
        switch (code) {
          case OpCode.POP:
            Fiber.sp--;
            break;
          case OpCode.CLOSE_UPVALUE:
            CloseUpvalue();
            break;
          case OpCode.POP_UNDER:
            Fiber.sp -= NextInt + 1; // + 1 is to pop the value on the fiber.stack as well
            break;
          default:
            throw new Exception("Unexpected code in return scope closing patch: " + code);
        }
      }
      Fiber.Fp--;
      if (Fiber.Fp == 0) { // returning from top-level func
        Fiber.sp = liveInstanceStartingFiberSp;
        if (Fiber.caller != null) {
          Fiber.state = Fiber.State.NEW;
        }
        Push(result);
        return true;
      } else {
        Fiber.sp = returningFrame.Sp;
        Push(result);
        return Fiber.Fp == breakAtFp;
      }
    }

    private IFielded BoxIfNotInstance(int offset) {
      var loc = Fiber.sp - offset - 1;
      var value = Fiber.stack[loc];
      if (value == Nil.INSTANCE) {
        return Nil.INSTANCE;
      } else if (value is IFielded f) {
        return f;
      } else if (value is Dictionary<string, object> dict) {
        return new Instance(this, CoreClasses.CLASS_OBJECT, dict);
      } else if (value is bool) {
        var boxed = new Instance(this, CoreClasses.CLASS_BOOL, new Dictionary<string, object> {
                    { Constants.PRIVATE, value }
                });
        Fiber.stack[loc] = boxed;
        return boxed;
      } else if (value is long) {
        var boxed = new Instance(this, CoreClasses.CLASS_INT, new Dictionary<string, object> {
                    { Constants.PRIVATE, value }
                });
        Fiber.stack[loc] = boxed;
        return boxed;
      } else if (value is double) {
        var boxed = new Instance(this, CoreClasses.CLASS_FLOAT, new Dictionary<string, object> {
                    { Constants.PRIVATE, value }
                });
        Fiber.stack[loc] = boxed;
        return boxed;
      } else if (value is string) {
        var boxed = new Instance(this, CoreClasses.CLASS_STR, new Dictionary<string, object> {
                    { Constants.PRIVATE, value }
                });
        Fiber.stack[loc] = boxed;
        return boxed;
      } else {
        throw RuntimeError($"Unable to box {value}");
      }
    }

    private object Unbox(object o) {
      if (o is Instance instance) {
        if (instance.Class == CoreClasses.CLASS_INT || instance.Class == CoreClasses.CLASS_FLOAT || instance.Class == CoreClasses.CLASS_STR) {
          return instance.Fields[Constants.PRIVATE];
        }
      }
      return o;
    }

    public string Stringify(object o, bool forPrinting = false) {
      if (o is Instance instance) {
        if (instance.Class == CoreClasses.CLASS_STR || instance.Class == CoreClasses.CLASS_INT || instance.Class == CoreClasses.CLASS_FLOAT) {
          return Stringify(instance.Fields[Constants.PRIVATE], forPrinting);
        } else {
          return ((IFielded)instance).Stringify(this);
        }
      } else if (o is IFielded f) {
        return f.Stringify(this);
      } else if (o is Dictionary<string, object> dict) {
        return ((IFielded)new Instance(this, CoreClasses.CLASS_OBJECT, dict)).Stringify(this);
      } else if (o is string s) {
        return forPrinting ? $"\"{o}\"" : s;
      } else if (o is Thrown t) {
        return $"!thrown! {Stringify(t.Value, forPrinting)}";
      } else if (o == null) {
        return Nil.INSTANCE.ToString();
      } else {
        return o.ToString();
      }
    }

    public bool HasClass(string name, ClassKind kind) {
      var klass = declaredClasses.GetValue(name);
      if (klass == null) {
        return false;
      }
      return klass.Kind == kind;
    }

    public List<string> GetClassesOfKind(ClassKind kind) => declaredClasses.Where(c => c.Value.Kind == kind).Select(c => c.Key).ToList();

    public Exception RuntimeError(string message) {
      Console.WriteLine();
      Console.WriteLine(message);
      PrintCallStack();
      var exception = new Exception(message);
      Debugger?.TriggerError(exception);
      return exception;
    }

    internal string GetCallStack() {
      var cs = "";
      for (var i = Fiber.Fp - 1; i >= 0; i--) {
        cs += Fiber.callFrames[i].ToString() + "\n";
      }
      return cs;
    }

    private void PrintCallStack() {
      Console.WriteLine(GetCallStack());
    }
  }
}