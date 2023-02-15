using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NusantaraScript {
    public interface IFielded {
        Dictionary<string, object> Fields { get; }

        string Stringify(Vm vm) {
            return new StringBuilder()
                .Append(TokenType.LEFT_BRACKET.ToCode())
                .Append(string.Join(", ", Fields.Select(it => $"{it.Key}{TokenType.COLON.ToCode()} {vm.Stringify(it.Value, true)}")))
                .Append(TokenType.RIGHT_BRACKET.ToCode())
                .ToString();
        }
    }

    public class SClass : IFielded {
        public Dictionary<string, object> Fields { get; }
        public Dictionary<string, SClass> Superclasses { get; }

        public string Name { get; }
        public ClassKind Kind { get; }

        public SClass(string name, ClassKind kind, Dictionary<string, object> fields = null, 
                Dictionary<string, SClass> superclasses = null) {
            Name = name;
            Kind = kind;
            Fields = fields ?? new Dictionary<string, object>();
            Superclasses = superclasses ?? new Dictionary<string, SClass>();
        }

        public SClass GetSuperclass(string name) {
            foreach (var superclass in Superclasses?.Values) {
                if (superclass.Name == name) {
                    return superclass;
                }
                var superSuperclass = superclass.GetSuperclass(name);
                if (superSuperclass != null) {
                    return superSuperclass;
                }
            }
            return null;
        }

        public bool CheckIs(SClass other) {
            if (this == other) {
                return true;
            }
            foreach (var superclass in Superclasses.Values) {
                if (superclass == other) {
                    return true;
                }
                if (superclass.CheckIs(other)) {
                    return true;
                }
            }
            return false;
        }

        public override string ToString() {
            return $"{Kind} {Name}";
        }
    }

    public enum ClassKind {
        CORE, CUSTOM, EFFECT, STANCE, SCENARIO, AI
    }

    public static class ClassKindExtensions {
        public static byte Byte(this ClassKind kind) => (byte) kind;

        public static ClassKind From(byte b) {
            return EnumExtensions.GetValues<ClassKind>()[b];
        }
    }

    public class Nil : IFielded {
        public static readonly Nil INSTANCE = new();

        private readonly Dictionary<string, object> fields = new();
        public Dictionary<string, object> Fields => fields;

        private Nil() { }

        public override string ToString() => TokenType.NULL.ToCode();
    }
}