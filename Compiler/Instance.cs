using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NusantaraScript {
  public class Instance : IFielded {
    public Vm Vm { get; }
    public SClass Class { get; }

    public Dictionary<string, object> Fields { get; }

    public Instance(Vm vm, SClass klass, Dictionary<string, object> fields = null) {
      Vm = vm;
      Class = klass;
      Fields = fields ?? new Dictionary<string, object>();
    }

    public virtual object this[string key] {
      get => Fields.GetValue(key);
      set {
        if (value == Nil.INSTANCE) {
          Fields.Remove(key);
        } else {
          Fields[key] = value;
        }
      }
    }

    public static Instance FromFields(Vm vm, Dictionary<string, object> fields) => new Instance(vm, CoreClasses.CLASS_OBJECT, fields);
  }

  public class ListInstance : Instance, IFielded {
    public List<object> Items { get; }

    public ListInstance(Vm vm, List<object> providedItems) : base(vm, CoreClasses.CLASS_LIST) {
      Items = providedItems ?? new List<object>();
    }

    public override object this[string key] {
      get {
        throw Vm.RuntimeError($"Invalid key in List: {key}");
      }
      set {
        throw Vm.RuntimeError("Can't set keys on a list!");
      }
    }

    public object this[int index] {
      get {
        if (index >= Items.Count) {
          throw Vm.RuntimeError($"Illegal argument error, index: {index}, size: {Items.Count}");
        } else if (index >= 0) {
          return Items[index];
        } else {
          return Items[Items.Count + index];
        }
      }
      set {
        if (index >= Items.Count) {
          throw Vm.RuntimeError($"Illegal argument error, index: {index}, size: {Items.Count}");
        } else if (index >= 0) {
          Items[index] = value;
        } else {
          Items[Items.Count + index] = value;
        }
      }
    }

    public string Stringify(Vm vm) {
      return new StringBuilder()
          .Append(TokenType.LEFT_BRACKET.ToCode())
          .Append(string.Join(", ", Items.Select(it => vm.Stringify(it, true))))
          .Append(TokenType.RIGHT_BRACKET.ToCode())
          .ToString();
    }
  }
}