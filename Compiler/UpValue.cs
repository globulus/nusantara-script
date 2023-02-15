using System;

namespace NusantaraScript {
  public class UpValue {
    public object Location { get; set; }
    public string FiberName { get; }
    public bool Closed { get; set; }
    public UpValue Next { get; set; }

    public UpValue(object location, string fiberName, bool closed, UpValue next) {
      Location = location;
      FiberName = fiberName;
      Closed = closed;
      Next = next;
    }

    public int Sp {
      get {
        var loc = Location;
        if (loc is int i) {
          return i;
        } else {
          throw new InvalidCastException("Trying to read Upvalue sp when it's already closed");
        }

      }
    }
  }
}