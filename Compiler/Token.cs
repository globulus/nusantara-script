namespace NusantaraScript {
  public class Token {
    public TokenType Type { get; }
    public string Lexeme { get; }
    public IPrimitiveValue Literal { get; }
    public int Line { get; }
    public string File { get; }
    public bool HasBreakpoint { get; set; }

    public Token(TokenType type, string lexeme, IPrimitiveValue literal, int line, string file) {
      Type = type;
      Lexeme = lexeme;
      Literal = literal;
      Line = line;
      File = file;
      HasBreakpoint = false;
    }

    public Token Copy(TokenType newType) {
      return new Token(newType, Lexeme, Literal, Line, File);
    }

    public override string ToString() {
      return $"{Type} {Lexeme} {Literal}";
    }

    public class Factory {
      private Token opener;

      public Factory(Token opener) {
        this.opener = opener;
      }

      public Token OfType(TokenType type) {
        return new Token(type, null, null, opener.Line, opener.File);
      }

      public Token This() {
        return new Token(TokenType.THIS, Constants.THIS, null, opener.Line, opener.File);
      }

      public Token Named(string name) {
        return new Token(TokenType.IDENTIFIER, name, null, opener.Line, opener.File);
      }

      public Token OfString(string value) {
        return new Token(TokenType.STRING, value, new Str(value), opener.Line, opener.File);
      }

      public Token OfLong(long value) {
        return new Token(TokenType.NUMBER, "" + value, new Int(value), opener.Line, opener.File);
      }
    }
  }
}