using System;

namespace NusantaraScript {
  public enum TokenType {
    LEFT_PAREN, RIGHT_PAREN,
    LEFT_BRACKET, RIGHT_BRACKET,
    LEFT_BRACE, RIGHT_BRACE,
    COMMA,
    DOT, QUESTION_DOT, DOT_COMMA, QUESTION_DOT_COMMA,
    DOT_DOT, DOT_DOT_DOT,
    COLON, COLON_COLON,
    NEWLINE,

    BANG, BANG_EQUAL,
    EQUAL, EQUAL_EQUAL, EQUAL_GREATER,
    GREATER, GREATER_EQUAL,
    LESS, LESS_EQUAL,
    PLUS, PLUS_EQUAL,
    AND_AND, OR_OR,
    MINUS, MINUS_EQUAL,
    STAR, STAR_STAR, STAR_EQUAL,
    SLASH, SLASH_SLASH, SLASH_EQUAL, SLASH_SLASH_EQUAL,

    MOD, MOD_MOD, MOD_EQUAL,
    QUESTION_QUESTION, QUESTION_BANG,

    // Literals.
    IDENTIFIER, STRING, NUMBER,

    // Keywords.
    BREAK, CONTINUE, ELSE, THIS, SUPER,
    FALSE, FOR, IF, NULL, FN,
    RETURN, TRUE, WHILE, IN, IMPORT,
    WHEN, IS, NOT_IN, NOT_IS, THROW,
    CATCH, CLASS,

    // Script-specific keywords
    EFFECT, STANCE, SCENARIO, AI,

    EOF,
  }

  public static class TokenTypeEtensions {
    public static string ToCode(this TokenType type) {
      switch (type) {
        case TokenType.LEFT_PAREN:
          return "(";
        case TokenType.RIGHT_PAREN:
          return ")";
        case TokenType.LEFT_BRACKET:
          return "[";
        case TokenType.RIGHT_BRACKET:
          return "]";
        case TokenType.LEFT_BRACE:
          return "{";
        case TokenType.RIGHT_BRACE:
          return "}";
        case TokenType.COMMA:
          return ",";
        case TokenType.DOT:
          return ".";
        case TokenType.QUESTION_DOT:
          return "?.";
        case TokenType.DOT_COMMA:
          return ".,";
        case TokenType.QUESTION_DOT_COMMA:
          return "?.,";
        case TokenType.DOT_DOT:
          return "..";
        case TokenType.DOT_DOT_DOT:
          return "...";
        case TokenType.COLON:
          return ":";
        case TokenType.COLON_COLON:
          return "::";
        case TokenType.NEWLINE:
          return "\n";
        case TokenType.BANG:
          return "!";
        case TokenType.BANG_EQUAL:
          return "!=";
        case TokenType.EQUAL:
          return "=";
        case TokenType.EQUAL_EQUAL:
          return "==";
        case TokenType.EQUAL_GREATER:
          return "=>";
        case TokenType.GREATER:
          return ">";
        case TokenType.GREATER_EQUAL:
          return ">=";
        case TokenType.LESS:
          return "<";
        case TokenType.LESS_EQUAL:
          return "<=";
        case TokenType.PLUS:
          return "+";
        case TokenType.PLUS_EQUAL:
          return "+=";
        case TokenType.AND_AND:
          return "&&";
        case TokenType.OR_OR:
          return "||";
        case TokenType.MINUS:
          return "-";
        case TokenType.MINUS_EQUAL:
          return "-=";
        case TokenType.STAR:
          return "*";
        case TokenType.STAR_STAR:
          return "**";
        case TokenType.STAR_EQUAL:
          return "*=";
        case TokenType.SLASH:
          return "/";
        case TokenType.SLASH_SLASH:
          return "//";
        case TokenType.SLASH_EQUAL:
          return "/=";
        case TokenType.SLASH_SLASH_EQUAL:
          return "//=";
        case TokenType.MOD:
          return "%";
        case TokenType.MOD_MOD:
          return "%%";
        case TokenType.MOD_EQUAL:
          return "%=";
        case TokenType.QUESTION_QUESTION:
          return "??";
        case TokenType.QUESTION_BANG:
          return "?!";
        case TokenType.IDENTIFIER:
        case TokenType.STRING:
        case TokenType.NUMBER:
        case TokenType.EOF:
          throw new Exception("Shouldn't be used with toCode()");
        default:
          return type.ToString().ToLower();
      }
    }
  }
}