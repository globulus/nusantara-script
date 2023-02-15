using System;
using System.Collections.Generic;
using System.Linq;

namespace NusantaraScript {
    public static class CoreClasses {
        public static SClass CLASS_BOOL = new SClass(Constants.CLASS_BOOL, ClassKind.CORE, new Dictionary<string, object>() {
        });
        public static SClass CLASS_INT = new SClass(Constants.CLASS_INT, ClassKind.CORE, new Dictionary<string, object>() {
            { "times", new NativeFunction(0, args => {
                var instance = (Instance) args[0];
                var value = (long) instance.Fields[Constants.PRIVATE];
                return instance.Vm.CreateRange(0, value - 1);
            }) },
            { "min", new NativeFunction(0, args => long.MinValue) },
            { "max", new NativeFunction(0, args => long.MaxValue) },
        });

        public static SClass CLASS_FLOAT = new SClass(Constants.CLASS_FLOAT, ClassKind.CORE, new Dictionary<string, object>() {
            { "min", new NativeFunction(0, args => double.MinValue) },
            { "max", new NativeFunction(0, args => double.MaxValue) },
        });

        public static SClass CLASS_STR = new SClass(Constants.CLASS_STR, ClassKind.CORE, new Dictionary<string, object>() {
            { "length", new NativeFunction(1, args => {
                var str = StringValue(args);
                return (long) str.Length;
            }) },
            { Constants.HAS, new NativeFunction(1, args => {
                var str = StringValue(args);
                var other = (string) args[1];
                return str.Contains(other);
            }) },
            { Constants.ITERATE, new NativeFunction(0, args => {
                var str = StringValue(args);
                var charList = new ListInstance(((Instance) args[0]).Vm, str.ToCharArray().Select(c => c.ToString()).ToList<object>());
                return new ListIterator(charList);
            }) },
            { "format", new NativeFunction(1, args => {
                var str = StringValue(args);
                var argList = (ListInstance) args[1];
                return string.Format(str, argList.Items.ToArray<object>());
            }) },
            { "isEmpty", new NativeFunction(0, args => {
                var str = StringValue(args);
                return str.Length == 0;
            }) },
            { "startsWith", new NativeFunction(1, args => {
                var str = StringValue(args);
                var other = (string)args[1];
                return str.StartsWith(other);
            }) },
            { "endsWith", new NativeFunction(1, args => {
                var str = StringValue(args);
                var other = (string)args[1];
                return str.EndsWith(other);
            }) },
            { "lowercased", new NativeFunction(0, args => {
                var str = StringValue(args);
                return str.ToLowerInvariant();
            }) },
            { "uppercased", new NativeFunction(0, args => {
                var str = StringValue(args);
                return str.ToUpperInvariant();
            }) },
        });

        public static SClass CLASS_OBJECT = new SClass(Constants.CLASS_OBJECT, ClassKind.CORE, new Dictionary<string, object>() {
            { "keys", new NativeFunction(0, args => {
                var instance = (Instance) args[0];
                return new ListInstance(instance.Vm, instance.Fields.Keys.ToList<object>());
            }) }
        });

        public static SClass CLASS_LIST = new SClass(Constants.CLASS_LIST, ClassKind.CORE, new Dictionary<string, object>() {
            { "size", new NativeFunction(0, args => {
                var instance = (ListInstance) args[0];
                return (long) instance.Items.Count;
            }) },
            { "isEmpty", new NativeFunction(0, args => {
                var instance = (ListInstance) args[0];
                return !instance.Items.Any();
            }) },
            { Constants.HAS, new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var other = args[1];
                return instance.Items.Contains(other);
            }) },
            { Constants.ITERATE, new NativeFunction(0, args => {
                var instance = (ListInstance) args[0];
                return new ListIterator(instance);
            }) },
            { "add", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var item = args[1];
                instance.Items.Add(item);
                return instance;
            }) },
            { "addAll", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var other = (ListInstance) args[1];
                instance.Items.AddRange(other.Items);
                return instance;
            }) },
            { "insert", new NativeFunction(2, args => {
                var instance = (ListInstance) args[0];
                var item = (ListInstance) args[1];
                var index = (int) args[2];
                instance.Items.Insert(index, item);
                return instance;
            }) },
            { "removeAt", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var index = (int) args[1];
                instance.Items.RemoveAt(index);
                return instance;
            }) },
            { "clear", new NativeFunction(0, args => {
                var instance = (ListInstance) args[0];
                instance.Items.Clear();
                return instance;
            }) },
            { "indexOf", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var item = args[1];
                var index = instance.Items.IndexOf(item);
                return (index == -1) ? Nil.INSTANCE : index;
            }) },
            { "lastIndexOf", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var item = args[1];
                var index = instance.Items.LastIndexOf(item);
                return (index == -1) ? Nil.INSTANCE : index;
            }) },
            { "sublist", new NativeFunction(2, args => {
                var instance = (ListInstance) args[0];
                var index = Convert.ToInt32(args[1]);
                var count = Convert.ToInt32(args[2]);
                return new ListInstance(instance.Vm, instance.Items.GetRange(index, count));
            }) },
            { "take", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var count = Convert.ToInt32(args[1]);
                return new ListInstance(instance.Vm, instance.Items.Take(count).ToList());
            }) },
            { "where", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var predicate = (Closure) args[1];
                var vm = instance.Vm;
                var filteredItems = instance.Items.Where(x => vm.IsTrue(vm.CallFromNative(predicate, x))).ToList();
                return new ListInstance(vm, filteredItems);
            }) },
            { "count", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var predicate = (Closure) args[1];
                var vm = instance.Vm;
                var count = instance.Items.Count(x => vm.IsTrue(vm.CallFromNative(predicate, x)));
                return (long) count;
            }) },
            { "any", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var predicate = (Closure) args[1];
                var vm = instance.Vm;
                return instance.Items.Any(x => vm.IsTrue(vm.CallFromNative(predicate, x)));
            }) },
            { "all", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var predicate = (Closure) args[1];
                var vm = instance.Vm;
                return instance.Items.All(x => vm.IsTrue(vm.CallFromNative(predicate, x)));
            }) },
            { "first", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var predicate = (Closure) args[1];
                var vm = instance.Vm;
                return instance.Items.FirstOrDefault(x => vm.IsTrue(vm.CallFromNative(predicate, x)));
            }) },
            { "last", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var predicate = (Closure) args[1];
                var vm = instance.Vm;
                return instance.Items.LastOrDefault(x => vm.IsTrue(vm.CallFromNative(predicate, x)));
            }) },
            { "map", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var transform = (Closure) args[1];
                var vm = instance.Vm;
                var mappedItems = instance.Items.Select(x => vm.CallFromNative(transform, x)).ToList();
                return new ListInstance(vm, mappedItems);
            }) },
            { "mapToObject", new NativeFunction(2, args => {
                var instance = (ListInstance) args[0];
                var keySelector = (Closure) args[1];
                var valueSelector = (Closure) args[2];
                var vm = instance.Vm;
                return new Instance(vm, CLASS_OBJECT,
                    instance.Items.ToDictionary(
                        x => (string) vm.CallFromNative(keySelector, x),
                        x => vm.CallFromNative(valueSelector, x)
                    ));
            }) },
            { "compactMap", new NativeFunction(0, args => {
                var instance = (ListInstance) args[0];
                var vm = instance.Vm;
                var mappedItems = instance.Items.Where(x => x != null).ToList();
                return new ListInstance(vm, mappedItems);
            }) },
            { "reduce", new NativeFunction(2, args => {
                var instance = (ListInstance) args[0];
                var initialValue = args[1];
                var reducer = (Closure) args[2];
                var vm = instance.Vm;
                instance.Items.ForEach(i => {
                    initialValue = vm.CallFromNative(reducer, initialValue, i);
                });
                return initialValue;
            }) },
            { "groupBy", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var valueSelector = (Closure) args[1];
                var vm = instance.Vm;
                var groupedItems = instance.Items
                    .GroupBy(v => vm.CallFromNative(valueSelector, v))
                    .ToDictionary(g => vm.Stringify(g.Key),
                                  g => (object) new ListInstance(vm, g.ToList()));
                return new Instance(vm, CLASS_OBJECT, groupedItems);
            }) },
            { "sortedBy", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var comparator = (Closure) args[1];
                var vm = instance.Vm;
                var sortedItems = new List<object>(instance.Items);
                sortedItems.Sort((lhs, rhs) => Convert.ToInt32(vm.CallFromNative(comparator, lhs, rhs)));
                return new ListInstance(vm, sortedItems);
            }) },
            { "reversed", new NativeFunction(0, args => {
                var instance = (ListInstance) args[0];
                var vm = instance.Vm;
                var reversedItems = new List<object>(instance.Items);
                reversedItems.Reverse();
                return new ListInstance(vm, reversedItems);
            }) },
            { "shuffled", new NativeFunction(0, args => {
                var instance = (ListInstance) args[0];
                var vm = instance.Vm;
                var shuffledItems = instance.Items.Shuffled();
                return new ListInstance(vm, shuffledItems);
            }) },
            { "unique", new NativeFunction(0, args => {
                var instance = (ListInstance) args[0];
                var vm = instance.Vm;
                var uniqueItems = new HashSet<object>(instance.Items).ToList();
                return new ListInstance(vm, uniqueItems);
            }) },
            { "randomItem", new NativeFunction(0, args => {
                var instance = (ListInstance) args[0];
                if (instance.Items.Count == 0) {
                    return null;
                }
                return instance.Items[new Random().Next(instance.Items.Count)];
            }) },
            { "join", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var vm = instance.Vm;
                var separator = (string) args[1];
                return string.Join(separator, instance.Items.Select(x => vm.Stringify(x)));
            }) },
            { "minBy", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var transformer = (Closure) args[1];
                var vm = instance.Vm;
                double minValue = double.MaxValue;
                object minItem = null;
                foreach (var item in instance.Items) {
                    var value = Convert.ToDouble(vm.CallFromNative(transformer, item));
                    if (value < minValue) {
                        minValue = value;
                        minItem = item;
                    }
                }
                return minItem;
            }) },
            { "maxBy", new NativeFunction(1, args => {
                var instance = (ListInstance) args[0];
                var transformer = (Closure) args[1];
                var vm = instance.Vm;
                double maxValue = double.MinValue;
                object maxItem = null;
                foreach (var item in instance.Items) {
                    var value = Convert.ToDouble(vm.CallFromNative(transformer, item));
                    if (value > maxValue) {
                        maxValue = value;
                        maxItem = item;
                    }
                }
                return maxItem;
            }) },
        });

        internal class ListIterator : Instance {
            private ListInstance list;
            private int index = 0;

            internal ListIterator(ListInstance list) : base(list.Vm, CLASS_OBJECT) {
                this.list = list;
                Fields[Constants.NEXT] = new NativeFunction(0, args => {
                    if (index == list.Items.Count) {
                        return Nil.INSTANCE;
                    } else {
                        return list.Items[index++];
                    }
                });
            }
        }

        public static SClass CLASS_RANGE = new SClass(Constants.CLASS_RANGE, ClassKind.CORE, new Dictionary<string, object>() {
            { Constants.INIT, new NativeFunction(2, args => {
                var instance = (Instance) args[0];
                var min = args[1];
                var max = args[2];
                instance.Fields["min"] = min;
                instance.Fields["max"] = max;
                return instance;
            }) },
            { Constants.HAS, new NativeFunction(1, args => {
                var instance = (Instance) args[0];
                object value = (args[1] as double?) ??  (long) args[1];
                var min = (long) instance.Fields.GetValue("min");
                var max = (long) instance.Fields.GetValue("max");
                return min <= (double) value && (double) value <= max;
            }) },
            { Constants.ITERATE, new NativeFunction(0, args => {
                var instance = (Instance) args[0];
                return new RangeIterator(instance);
            }) },
        });

        internal class RangeIterator : Instance {
            private Instance instance;
            private long value;

            internal RangeIterator(Instance instance) : base(instance.Vm, CLASS_OBJECT) {
                this.instance = instance;
                value = (long) instance.Fields.GetValue("min");
                var max = (long) instance.Fields.GetValue("max");
                Fields[Constants.NEXT] = new NativeFunction(0, args => {
                    if (value > max) {
                        return Nil.INSTANCE;
                    } else {
                        return value++;
                    }
                });
            }
        }

        private static string StringValue(List<object> args) {
            return (string) ((Instance) args[0]).Fields[Constants.PRIVATE];
        }
    }

    public static class CoreObjects {
        public static Instance GetConsoleObject(Vm vm) => new Instance(vm, CoreClasses.CLASS_OBJECT, new Dictionary<string, object> {
            { "print", new NativeFunction(1, args => {
                var text = vm.Stringify(args[1]);
                Console.Write(text);
                return args[0];
            }) },
            { "println", new NativeFunction(1, args => {
                var text = vm.Stringify(args[1]);
                Console.WriteLine(text);
                return args[0];
            }) },
            { "readln", new NativeFunction(0, args => {
                return Console.ReadLine();
            }) },
            { "clock", new NativeFunction(0, args => {
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }) },
        });

        public static Instance GetMathObject(Vm vm) => new Instance(vm, CoreClasses.CLASS_OBJECT, new Dictionary<string, object> {
            { "E", new NativeFunction(0, args => {
                return Math.E;
            }) },
            { "PI", new NativeFunction(0, args => {
                return Math.PI;
            }) },
            { "sin", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Sin(num);
            }) },
            { "cos", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Cos(num);
            }) },
            { "tan", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Tan(num);
            }) },
            { "asin", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Asin(num);
            }) },
            { "acos", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Acos(num);
            }) },
            { "atan", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Atan(num);
            }) },
            { "exp", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Exp(num);
            }) },
            { "sqrt", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Sqrt(num);
            }) },
            { "pow", new NativeFunction(2, args => {
                var num = Convert.ToDouble(args[1]);
                var p = Convert.ToDouble(args[2]);
                return Math.Pow(num, p);
            }) },
            { "ln", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Log(num);
            }) },
            { "log", new NativeFunction(2, args => {
                var num = Convert.ToDouble(args[1]);
                var b = Convert.ToDouble(args[2]);
                return Math.Log(num, b);
            }) },
            { "abs", new NativeFunction(1, args => {
                if (args[1] is long l) {
                    return Math.Abs(l);
                } else {
                    return Math.Abs((double) args[1]);
                }
            }) },
            { "min", new NativeFunction(2, args => {
                var a = Convert.ToDouble(args[1]);
                var b = Convert.ToDouble(args[2]);
                var r = Math.Min(a, b);
                if (args[1] is long && args[2] is long) {
                    return Convert.ToInt64(r);
                } else {
                    return r;
                }
            }) },
            { "max", new NativeFunction(2, args => {
                var a = Convert.ToDouble(args[1]);
                var b = Convert.ToDouble(args[2]);
                var r = Math.Max(a, b);
                if (args[1] is long && args[2] is long) {
                    return Convert.ToInt64(r);
                } else {
                    return r;
                }
            }) },
            { "round", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Convert.ToInt64(Math.Round(num));
            }) },
            { "ceil", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Ceiling(num);
            }) },
            { "floor", new NativeFunction(1, args => {
                var num = Convert.ToDouble(args[1]);
                return Math.Floor(num);
            }) },
            { "clamp", new NativeFunction(3, args => {
                var x = Convert.ToDouble(args[1]);
                var min = Convert.ToDouble(args[2]);
                var max = Convert.ToDouble(args[3]);
                var r = Math.Clamp(x, min, max);
                if (args[1] is long && args[2] is long && args[3] is long) {
                    return Convert.ToInt64(r);
                } else {
                    return r;
                }
            }) },
            { "randomInt", new NativeFunction(2, args => {
                var lo = Convert.ToInt32(args[1]);
                var hi = Convert.ToInt32(args[2]);
                return (long) new Random().Next(lo, hi);
            }) },
            { "randomFloat", new NativeFunction(0, args => new Random().NextDouble()) },
        });


    public static Instance GetDebugObject(Vm vm) => new Instance(vm, CoreClasses.CLASS_OBJECT, new Dictionary<string, object> {
            { "callStack", new NativeFunction(0, args => vm.GetCallStack()) }
        });
    }
}