using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace NusantaraScript {
    public class ScriptDriver {
        public (Vm, Function) BootVm(bool isDebug, Action<Exception> errorLogger, params string[] sourceFiles) {
            var allSourceImports = string.Join("\n", sourceFiles
                .Select(f => f.Replace(Path.DirectorySeparatorChar, '/'))
                .Select(f => $"import \"{f}\""));
            var lexer = new Lexer("Script", allSourceImports, isDebug);
            var lexerOutput = lexer.ScanTokens(true);
            var tokens = new List<Token>();
            ScanImports(Directory.GetCurrentDirectory().Replace(Path.DirectorySeparatorChar, '/'), 
                lexerOutput, tokens, new List<string>(), isDebug);
            var compiler = new Compiler(isDebug);
            var compilerOutput = compiler.Compile(tokens,
                Constants.CLASS_BOOL, Constants.CLASS_INT, Constants.CLASS_FLOAT, Constants.CLASS_STR, Constants.CLASS_OBJECT,
                Constants.CLASS_LIST, Constants.OBJ_CONSOLE, Constants.OBJ_MATH, Constants.OBJ_DEBUG
            );
            var vm = BootVm(isDebug, errorLogger, compilerOutput);
            return (vm, compilerOutput);
        }

        public Vm BootVm(bool isDebug, Action<Exception> errorLogger, Function compilerOutput) {
            var vm = new Vm();
            Debugger debugger = isDebug ? new Debugger(vm) : null;
            vm.Interpret(new Fiber(new Closure(compilerOutput)), debugger, errorLogger,
                CoreClasses.CLASS_BOOL, CoreClasses.CLASS_INT, CoreClasses.CLASS_FLOAT, CoreClasses.CLASS_STR,
                CoreClasses.CLASS_OBJECT, CoreClasses.CLASS_LIST,
                CoreObjects.GetConsoleObject(vm), CoreObjects.GetMathObject(vm), CoreObjects.GetDebugObject(vm)
            );
            return vm;
        }

        private void ScanImports(string sourcePath, LexerOutput lexerOutput, 
                List<Token> allTokens, List<string> imports, bool isDebug) {
            var lexerTokens = lexerOutput.Tokens;
            var scriptImports = lexerOutput.Imports;
            foreach (var import in scriptImports) {
                var location = ConvertImportToLocation(sourcePath, import, "ns", imports);
                if (location != null) {
                    var nativeLocation = location.Replace('/', Path.DirectorySeparatorChar);
                    var parent = Directory.GetParent(nativeLocation);
                    if (parent != null) {
                        ScanImports(parent.FullName.Replace(Path.DirectorySeparatorChar, '/'), 
                            new Lexer(import, 
                                File.ReadAllText(nativeLocation).Replace("\r\n", "\n"),
                                isDebug).ScanTokens(false),
                            allTokens, imports, isDebug);
                    }
                }
            }
            allTokens.AddRange(lexerTokens);
        }

        private string ConvertImportToLocation(string sourcePath, string import,
                string extension, List<string> imports) {
            var fileName = import.Replace('/', Path.DirectorySeparatorChar);
            var importWithExtension = import;
            if (!fileName.EndsWith(extension)) {
                fileName += "." + extension;
                importWithExtension += "." + extension;
            }
            var location = Path.GetFullPath(File.Exists(fileName) ? importWithExtension : $"{sourcePath}/{importWithExtension}");
            if (imports.Contains(location)) {
                return null;
            } else {
                imports.Add(location);
                return location;
            }
        }
    }
}