using System;
using System.Collections.Generic;

namespace NusantaraScript {
  public interface IOptionalParamsFunc {
    public const int DEFAULT_PARAMS_START = -1;

    int Arity { get; }
    int OptionalParamsStart { get; set; }
    object[] DefaultValues { get; set; }
  }

  public class Function : IOptionalParamsFunc {
    public string Name { get; }
    public int Arity { get; }
    public int UpvalueCount { get; }
    public byte[] Code { get; }
    public object[] Constants { get; }
    public int OptionalParamsStart { get; set; }
    public object[] DefaultValues { get; set; }

    public DebugInfo DebugInfo { get; }

    public Function(string name, int arity, int upvalueCount,
            byte[] code, object[] constants, DebugInfo debugInfo) {
      Name = name;
      Arity = arity;
      UpvalueCount = upvalueCount;
      Code = code;
      Constants = constants;
      DebugInfo = debugInfo;
      OptionalParamsStart = IOptionalParamsFunc.DEFAULT_PARAMS_START;
      DefaultValues = null;
    }
  }

  public interface ICallable { }

  public class Closure : ICallable {
    public Function Function { get; }
    public UpValue[] Upvalues { get; }

    public Closure(Function function) {
      Function = function;
      Upvalues = new UpValue[function.UpvalueCount];
    }
  }

  public interface INativeFunction : IOptionalParamsFunc, ICallable { }

  public class NativeFunction : INativeFunction {
    public int Arity { get; }
    public Func<List<object>, object> Func { get; }
    public int OptionalParamsStart { get; set; }
    public object[] DefaultValues { get; set; }

    public NativeFunction(int arity, Func<List<object>, object> func) {
      Arity = arity;
      Func = func;
      OptionalParamsStart = IOptionalParamsFunc.DEFAULT_PARAMS_START;
      DefaultValues = null;
    }
  }

  public abstract class BoundCallable : ICallable {
    public abstract object Receiver { get; }
    public abstract ICallable Callable { get; }

    public override string ToString() {
      return Callable.ToString();
    }
  }

  public class BoundMethod : BoundCallable {
    public override object Receiver { get; }
    public override ICallable Callable { get; }

    public Closure Closure => (Closure)Callable;

    public BoundMethod(object receiver, Closure callable) {
      Receiver = receiver;
      Callable = callable;
    }
  }

  public class BoundNativeMethod : BoundCallable {
    public override object Receiver { get; }
    public override ICallable Callable { get; }

    public INativeFunction NativeFunction => (INativeFunction)Callable;

    public BoundNativeMethod(object receiver, INativeFunction callable) {
      Receiver = receiver;
      Callable = callable;
    }
  }
}