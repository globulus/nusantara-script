using System;
using System.Collections.Generic;
using System.Linq;

namespace NusantaraScript {
  public static class Constants {
    public const string THIS = "this";
    public const string INIT = "init";
    public const string PRIVATE = "_";
    public const string HAS = "has";
    public const string ITERATE = "iterate";
    public const string NEXT = "next";

    public const string CLASS_STR = "Str";
    public const string CLASS_BOOL = "Bool";
    public const string CLASS_INT = "Int";
    public const string CLASS_FLOAT = "Float";
    public const string CLASS_OBJECT = "Object";
    public const string CLASS_LIST = "List";
    public const string CLASS_RANGE = "Range";

    public const string OBJ_CONSOLE = "Console";
    public const string OBJ_MATH = "Math";
    public const string OBJ_DEBUG = "Debug";

    public const string FIELD_ID = "id";
    public const string FIELD_TRIGGER = "trigger";
    public const string FIELD_TARGETS = "targets";
    public const string FIELD_RUN = "run";
    public const string FIELD_IS_ENABLED = "isEnabled";
    public const string FIELD_ON_START = "onStart";
    public const string FIELD_ON_STATE_CHANGE = "onStateChange";
    public const string FIELD_CHECK_VICTORY = "checkVictory";
    public const string FIELD_UNIT = "unit";
    public const string FIELD_DIFFICULTY = "difficulty";
    public const string FIELD_AWAKE = "awake";
    public const string FIELD_ON_GAME_START = "onGameStart";
    public const string FIELD_UPDATE = "update";
  }

  public static class DictionaryExtensions {
    public static TV GetValue<TK, TV>(this IDictionary<TK, TV> dict, TK key, TV defaultValue = default(TV)) {
      TV value;
      return dict.TryGetValue(key, out value) ? value : defaultValue;
    }

    public static void AddToSet<T>(this IDictionary<Function, HashSet<T>> dict, Function key, T item) {
      var set = dict.GetValue(key);
      if (set == null) {
        set = new HashSet<T>();
        dict[key] = set;
      }
      set.Add(item);
    }
  }

  public static class EnumExtensions {
    public static T[] GetValues<T>() {
      return (T[])Enum.GetValues(typeof(T));
    }
  }

  public static class ListExtensions {
    public static int Put(this List<byte> list, byte b) {
      list.Add(b);
      return 1;
    }
    public static int PutBool(this List<byte> list, bool b) {
      list.Add((byte)(b ? 1 : 0));
      return 1;
    }

    public static int Put(this List<byte> list, OpCode opCode) {
      list.Add(opCode.Byte());
      return 1;
    }

    public static int Put(this List<byte> list, byte[] bytes) {
      foreach (var b in bytes) {
        list.Add(b);
      }
      return bytes.Length;
    }

    public static byte[] toByteArray(this int i) {
      return BitConverter.GetBytes(i);
    }

    public static int PutInt(this List<byte> list, int i) {
      return list.Put(i.toByteArray());
    }

    public static void SetInt(this List<byte> list, int i, int pos) {
      var ba = i.toByteArray();
      for (var j = 0; j < ba.Length; j++) {
        list[pos + j] = ba[j];
      }
    }

    public static int PutLong(this List<byte> list, long l) {
      return list.Put(BitConverter.GetBytes(l));
    }

    public static int PutDouble(this List<byte> list, double d) {
      return list.Put(BitConverter.GetBytes(d));
    }

    public static List<T> Shuffled<T>(this List<T> list) {
      var random = new Random();
      return list.OrderBy(a => random.Next()).ToList();
    }

    public static T RandomItem<T>(this List<T> list) {
      if (list.Count == 0) {
        return default;
      }
      return list[new Random().Next(list.Count)];
    }

    public static T RandomItem<T>(this T[] array) {
      if (array.Length == 0) {
        return default;
      }
      return array[new Random().Next(array.Length)];
    }

    public static T RandomItem<T>(this IEnumerable<T> enumerable) {
      if (!enumerable.Any()) {
        return default;
      }
      return enumerable.ElementAt(new Random().Next(enumerable.Count()));
    }

    public static T[][] GetKCombs<T>(this IEnumerable<T> list, int length) where T : IComparable {
      if (length == 1) {
        return list.Select(t => new T[] { t }).ToArray();
      }
      return GetKCombs(list, length - 1)
        .SelectMany(t => list.Where(o => o.CompareTo(t.Last()) > 0), (t1, t2) => t1.Concat(new T[] { t2 }).ToArray())
        .ToArray();
    }
  }
}