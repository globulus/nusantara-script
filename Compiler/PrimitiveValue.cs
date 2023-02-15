namespace NusantaraScript {
  public interface IPrimitiveValue { }

  public struct Str : IPrimitiveValue {
    public string Value { get; }

    public Str(string value) {
      Value = value;
    }
  }

  public struct Int : IPrimitiveValue {
    public long Value { get; }

    public Int(long value) {
      Value = value;
    }
  }

  public struct Float : IPrimitiveValue {
    public double Value { get; }

    public Float(double value) {
      Value = value;
    }
  }
}