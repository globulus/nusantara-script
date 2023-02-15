using System.Collections.Generic;
using System.Text;

namespace NusantaraScript {
  public static class TokenPatcher {
    private const int SURROUNDING_LINES_COUNT = 2;

    public static string Patch(List<Token> tokens, Token highlighted) {
      var targetLine = highlighted?.Line ?? -1;
      var sb = new StringBuilder();
      var totalLenByLineStart = 0;
      var highlightPosition = 0;
      var printLineNumber = true;
      foreach (var token in tokens) {
        if (token.File != highlighted?.File) {
          continue;
        }
        var line = token.Line;
        if (line < targetLine - SURROUNDING_LINES_COUNT) {
          continue;
        } else if (line > targetLine + SURROUNDING_LINES_COUNT) {
          break;
        }
        sb.Append(printLineNumber ? $"[{line}] " : SpaceBefore(token));
        var isNewLine = token.Type == TokenType.NEWLINE;
        if (token == highlighted) {
          highlightPosition = sb.Length - totalLenByLineStart - 1;
        } else if (isNewLine) {
          if (line == targetLine - 1) {
            totalLenByLineStart = sb.Length;
          } else if (line == targetLine) {
            sb.Append('\n')
                .Append((highlightPosition == -1) ? "" : new string(' ', highlightPosition))
                .Append('^');
          }
        }
        sb.Append(TokenCode(token));
        printLineNumber = isNewLine;
      }
      return sb.ToString();
    }

    private static string TokenCode(Token token) {
      try {
        return token.Type.ToCode();
      } catch {
        return token.Lexeme;
      }
    }

    private static string SpaceBefore(Token token) {
      switch (token.Type) {
        case TokenType.DOT:
        case TokenType.DOT_DOT:
        case TokenType.DOT_DOT_DOT:
        case TokenType.LEFT_PAREN:
        case TokenType.RIGHT_PAREN:
        case TokenType.LEFT_BRACKET:
        case TokenType.RIGHT_BRACKET:
          return "";
        default:
          return " ";
      }
    }
  }
}