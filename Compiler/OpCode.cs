namespace NusantaraScript {
  public enum OpCode {
    UNKNOWN,
    TRUE,
    FALSE,
    CONST_INT,
    CONST_FLOAT,
    CONST_ID,
    CONST,
    NIL,
    POP,
    POP_UNDER, // Keeps the top value and pops N values beneath it
    DUPLICATE, // Duplicates the value at the top of the stack
    SET_LOCAL,
    GET_LOCAL,
    SET_UPVALUE,
    GET_UPVALUE,
    SET_PROP,
    GET_PROP,
    UPDATE_PROP,
    LT,
    LE,
    GT,
    GE,
    EQ,
    IS,
    INVERT,
    NEGATE,
    ADD,
    SUBTRACT,
    MULTIPLY,
    DIVIDE,
    DIVIDE_INT,
    MOD,
    HAS, // Requires a special OpCode as the operands are inverted
    PRINT,
    JUMP,
    JUMP_IF_FALSE,
    JUMP_IF_NIL,
    JUMP_IF_THROWN,
    CALL,
    INVOKE,
    CLOSURE,
    CLOSE_UPVALUE,
    RETURN,
    THROW,
    CLASS,
    INHERIT,
    METHOD,
    FIELD,
    CLASS_DECLR_DONE,
    SUPER,
    GET_SUPER,
    SUPER_INVOKE,
    OBJECT,
    LIST,
    RANGE_TO,
    UNBOX_THROWN
  }

  public static class OpCodeExtensions {
    public static byte Byte(this OpCode opCode) => (byte)opCode;

    public static OpCode From(byte b) {
      return EnumExtensions.GetValues<OpCode>()[b];
    }
  }
}