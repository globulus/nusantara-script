using System.IO;
using System.Linq;
using MiscUtil.Conversion;
using MiscUtil.IO;

namespace NusantaraScript {
  public class Fiber {
    public Closure Closure { get; }

    public State state = State.NEW;
    public int sp = 0;
    public int stackSize = Vm.INITIAL_STACK_SIZE;
    public object[] stack = new object[Vm.INITIAL_STACK_SIZE];
    public CallFrame frame;
    public CallFrame[] callFrames = new CallFrame[Vm.MAX_FRAMES];

    private int fp = 0;
    public int Fp {
      get => fp;
      set {
        fp = value;
        if (value > 0) {
          frame = callFrames[value - 1];
        }
      }
    }

    public Fiber caller = null;
    public string Name => Closure.Function.Name;

    public Fiber(Closure closure) {
      Closure = closure;
    }

    public void SetSpToEndOfStack() {
      for (var i = stackSize - 1; i >= 0; i--) {
        if (stack[i] != null && stack[i] != Nil.INSTANCE) {
          sp = i + 1;
          break;
        }
      }
    }

    public enum State {
      NEW, STARTED
    }
  }

  public class CallFrame {
    public Closure Closure { get; }
    public int Sp { get; }
    public EndianBinaryReader Buffer { get; }
    public string Name => Closure.Function.Name;

    public CallFrame(Closure closure, int sp) {
      Closure = closure;
      Sp = sp;
      Buffer = new EndianBinaryReader(EndianBitConverter.Little, new MemoryStream(Closure.Function.Code));
    }

    public CodePointer CurrentCodePoint {
      get {
        var debugInfo = Closure.Function.DebugInfo;
        if (debugInfo == null) {
          return CodePointer.UNKNOWN;
        }
        CodePointer? pointer = null;
        var pos = Buffer.Position;
        var sortedLines = Closure.Function.DebugInfo.Lines.OrderBy(e => e.Value);
        foreach (var e in sortedLines) {
          if (e.Value > pos) {
            if (pointer == null) {
              pointer = e.Key;
            }
            break;
          }
          pointer = e.Key;
        }
        if (pointer == null) {
          return CodePointer.UNKNOWN;
        }
        return (CodePointer)pointer;
      }
    }

    public override string ToString() {
      return $"[{CurrentCodePoint}] in {Closure.Function.Name}";
    }
  }
}