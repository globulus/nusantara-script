using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NusantaraScript {
    public class Debugger {
        public const string BREAKPOINT_LEXEME = "BP";
        private const int MAX_LOCALS = 5; // maximum number of locals to be printed out in regular mode
        private const int MAX_STACK_ITEMS = 5; // maximum number of stack items to be printed out in regular mode
        private const int MAX_VALUE_LENGTH = 100;
        private const string HELP = @"
            Commands:
                g index - (g)o to call frame at index.
                p index - (p)rints the value at the stack index.
                i - step (i)nto.
                v - step o(v)er.
                o - step (o)ut.
                l - print all (l)ocals.
                s - print the entire fiber (s)tack.
                a - add a breakpoint for current line.
                r - removes the breakpoint at current line.
        ";

        private readonly Vm vm;
        private IDebuggerFrontend frontend;
        private bool debuggingOff = false;
        private readonly Dictionary<Function, HashSet<long>> addedBreakpoints = new();
        private readonly Dictionary<Function, HashSet<long>> ignoredBreakpoints = new();
        private int focusFrame = 0;
        private StepStatus status = StepStatus.BREAKPOINT;
        private readonly Stack<CodePointer> triggerPoints = new();
        private readonly Stack<int> triggerFrames = new();

        private CodePointer CurrentCodePoint => vm.Fiber.frame.CurrentCodePoint;
        private long CurrentPosition => vm.Buffer.Position;
        private Function CurrentFunction => vm.CurrentFunction;
        private bool IsBreakpointIgnored => ignoredBreakpoints.GetValue(CurrentFunction)?.Contains(CurrentPosition) == true;

        public Debugger(Vm vm) {
            this.vm = vm;
        }

        public void SetFrontend(IDebuggerFrontend frontend) {
            this.frontend = frontend;
        }

        public void TriggerBreakpoint(bool isCall = false) {
            if (debuggingOff || frontend == null) {
                return;
            }
            focusFrame = 0;
            switch (status) {
            case StepStatus.BREAKPOINT: {
                var pos = CurrentPosition;
                var posHasBreakpoint = CurrentFunction.DebugInfo.Breakpoints.Contains(pos)
                    || addedBreakpoints.GetValue(CurrentFunction)?.Contains(pos) == true;
                if (posHasBreakpoint && IsBreakpointIgnored || !posHasBreakpoint) {
                    return;
                }
            } break;
            case StepStatus.INTO:
                if (!isCall) {
                    return;
                }
                status = StepStatus.BREAKPOINT;
                break;
            case StepStatus.OVER: {
                if (!triggerFrames.Any() || vm.Fiber.Fp > triggerFrames.Peek()) {
                    return;
                }
                if (!triggerPoints.Any() || CurrentCodePoint == triggerPoints.Peek()) {
                    return;
                }
                triggerPoints.Pop();
                triggerFrames.Pop();
                status = StepStatus.BREAKPOINT;
            } break;
            case StepStatus.OUT: {
                if (!triggerPoints.Any() || CurrentCodePoint != triggerPoints.Peek()) {
                    return;
                }
                triggerPoints.Pop();
                status = StepStatus.BREAKPOINT;
            } break;
            }
            focusFrame = 0;
            frontend.Prepare();
            PrintFocusFrame(true, "BREAKPOINT");
        }

        public void TriggerError(Exception e) {
            if (debuggingOff || frontend == null) {
                return;
            }
            focusFrame = 0;
            frontend.Prepare();
            PrintFocusFrame(true, "RUNTIME ERROR: " + e.Message);
        }

        private void PrintFocusFrame(bool readInput, string message) {
            var focusFrameIndex = vm.Fiber.Fp - 1 - focusFrame;
            var frame = vm.Fiber.callFrames[focusFrameIndex];
            var di = frame.Closure.Function.DebugInfo;
            var codePointer = frame.CurrentCodePoint;
            var tokens = di.Compiler.tokens.Where(t => t.File == codePointer.File).ToList();
            var highlightTokenIndex = tokens.FindIndex(t => t.Line == codePointer.Line);
            //var table = new ConsoleTable("Call stack", "Source code", "Locals", "Fiber stack");
            //table.Options.OutputTo = s => frontend?.Write(s);
            var callStack = GetCallStack(focusFrameIndex);
            var source = TokenPatcher.Patch(tokens, (highlightTokenIndex != -1) ? tokens[highlightTokenIndex] : null);
            var locals = GetLocalsForFrame(frame, codePointer/*, true*/);
            var stack = GetStackForFrame(frame, true);
            //table.AddRow(callStack, source, locals, stack);
            //table.Write();
            frontend?.WriteCallStack(callStack);
            frontend?.WriteSourceCode(source);
            frontend?.WriteLocals(locals);
            frontend?.WriteStack(stack);
            frontend?.WriteMessage(message);
            if (readInput) {
                ReadInput(frame, codePointer);
            }
        }

        private CallStack GetCallStack(int focusFrameIndex) {
            var frames = new string[vm.Fiber.Fp];
            var focusIndex = 0;
            for (int i = vm.Fiber.Fp - 1, j = 0; i >= 0; i--, j++) {
                if (i == focusFrameIndex) {
                    focusIndex = j;
                }
                frames[j] = vm.Fiber.callFrames[i].ToString();
            }
            return new CallStack {
                Frames = frames,
                FocusFrame = focusIndex,
            };
        }

        private FrameLocals GetLocalsForFrame(CallFrame frame, CodePointer codePointer) {
            var di = frame.Closure.Function.DebugInfo;
            var locals = di.Locals.OrderByDescending(it => it.Value.Start.Line);
            //var count = 0;
            var frameLocals = new List<FrameLocals.Local>();
            foreach (var pair in locals) { // reverse locals so that the most recently used one are at the top
                var lifetime = pair.Value;
                if (lifetime.Start.File == codePointer.File && lifetime.Start.Line > codePointer.Line) {
                    continue; // This one wasn't declared yet
                }
                if (lifetime.End?.File == codePointer.File && (lifetime.End?.Line ?? -1) < codePointer.Line) {
                    continue; // This one's already dead
                }
                var local = pair.Key;
                var value = vm.Fiber.stack[frame.Sp + local.Sp];
                if (value != null) {
                    //sb.AppendLine($"{local.Name} = {vm.Stringify(value)}");
                    frameLocals.Add(new FrameLocals.Local {
                        Name = local.Name,
                        ValueString = vm.Stringify(value),
                        ValueType = value switch {
                            bool => "Bool",
                            long => "Int",
                            double => "Float",
                            string => "Str",
                            ListInstance => "List",
                            Instance => "Object",
                            null or Nil => "null",
                            Closure => "fn",
                            _ => "Unknown",
                        },
                    });
                    //count++;
                } else {
                    break; // if we reached into the null territory of the stack (shouldn't happen, but still), break
                }
                //if (capped && count == MAX_LOCALS) {
                //    sb.AppendLine($"...{locals.Count() - MAX_LOCALS} more locals available, use 'l' to print them all.");
                //    break;
                //}
            }
            return new FrameLocals {
                Locals = frameLocals.ToArray(),
            };
        }

        private string GetStackForFrame(CallFrame frame, bool capped) {
            var sb = new StringBuilder();
            var count = 0;
            for (var i = vm.Fiber.sp - 1; i >= frame.Sp; i--) {
                sb.AppendLine($"[{i}] {LimitValue(vm.Stringify(vm.Fiber.stack[i]))}");
                count++;
                if (capped && count == MAX_STACK_ITEMS) {
                    sb.AppendLine($"...{vm.Fiber.sp - frame.Sp - MAX_STACK_ITEMS} more stack items available, use 's' to print them all.");
                    break;
                }
            }
            return sb.ToString();
        }

        private string LimitValue(string s) {
            if (s.Length > MAX_VALUE_LENGTH) {
                return s.Substring(0, MAX_VALUE_LENGTH) + "...";
            } else {
                return s;
            }
        }

        private void ReadInput(CallFrame frame, CodePointer codePointer) {
            bool waitingForInput = (frontend != null);
            while (waitingForInput) {
                switch (frontend?.Read()) {
                    case DebuggerCommand.PrintHelp:
                        frontend.WriteMessage(HELP);
                        break;
                    case DebuggerCommand.GoToFrame goToFrame:
                        focusFrame = goToFrame.Index;
                        PrintFocusFrame(false, "");
                        break;
                    case DebuggerCommand.PrintValueOnStack printValueOnStack:
                        frontend.WriteMessage(vm.Stringify(vm.Fiber.stack[printValueOnStack.Index]));
                        break;
                    case DebuggerCommand.StepInto:
                        status = StepStatus.INTO;
                        triggerPoints.Push(CurrentCodePoint);
                        waitingForInput = false;
                        break;
                    case DebuggerCommand.StepOver:
                        status = StepStatus.OVER;
                        triggerFrames.Push(vm.Fiber.Fp);
                        triggerPoints.Push(CurrentCodePoint);
                        waitingForInput = false;
                        break;
                    case DebuggerCommand.StepOut:
                        status = StepStatus.OUT;
                        waitingForInput = false;
                        break;
                    case DebuggerCommand.PrintLocals:
                        frontend.WriteLocals(GetLocalsForFrame(frame, codePointer/*, false*/));
                        break;
                    case DebuggerCommand.PrintStack:
                        frontend.WriteStack(GetStackForFrame(frame, false));
                        break;
                    case DebuggerCommand.AddBreakpoint:
                        addedBreakpoints.AddToSet(CurrentFunction, CurrentPosition);
                        frontend.WriteMessage($"Breakpoint added for {codePointer}.");
                        break;
                    case DebuggerCommand.RemoveBreakpoint:
                        var pos = CurrentPosition;
                        if (addedBreakpoints.GetValue(CurrentFunction)?.Contains(pos) == true) {
                            addedBreakpoints[CurrentFunction]?.Remove(pos);
                            frontend.WriteMessage($"Breakpoint removed for {codePointer}.");
                        } else {
                            ignoredBreakpoints.AddToSet(CurrentFunction, pos);
                            frontend.WriteMessage($"Breakpoint ignored for {codePointer}.");
                        }
                        break;
                    case DebuggerCommand.Resume:
                        status = StepStatus.BREAKPOINT;
                        waitingForInput = false;
                        break;
                    default:
                        break;
                }
            }
        }

        private enum StepStatus {
            BREAKPOINT, INTO, OVER, OUT
        }
    }

    public class DebugInfo {
        public Compiler Compiler { get; }
        public Lifetime Lifetime { get; } // which codepoint did the function def start and end when does it end
        public List<KeyValuePair<Compiler.Local, Lifetime>> Locals { get; } // list of all the locals ever and when were they discarded (at which line)
        public Dictionary<CodePointer, long> Lines { get; } // Maps lines to chunk positions - line X starts at position X
        public List<long> Breakpoints { get; } // List of bytecode positions that trigger breakpoints

        public DebugInfo(Compiler compiler, Lifetime lifetime, List<KeyValuePair<Compiler.Local, Lifetime>> locals,
                Dictionary<CodePointer, long> lines, List<long> breakpoints) {
            Compiler = compiler;
            Lifetime = lifetime;
            Locals = locals;
            Lines = lines;
            Breakpoints = breakpoints;
        }
    }

    public struct CodePointer {
        public static readonly CodePointer EMPTY = new(-1 , "");
        public static readonly CodePointer UNKNOWN = new(-1, "Unknown (enable debug mode)");

        public int Line { get; }
        public string File { get; }

        public CodePointer(int line, string file) {
            Line = line;
            File = file;
        }

        public CodePointer(Token token) : this(token.Line, token.File) { }

        public override string ToString() {
            return $"{File}:{Line}";
        }

        public override bool Equals(object obj) {
            if (obj == null || !(obj is CodePointer)) {
                return false;
            }
            var o = (CodePointer) obj;
            return File == o.File && Line == o.Line;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Line, File);
        }

        public static bool operator ==(CodePointer lhs, CodePointer rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(CodePointer lhs, CodePointer rhs) => !(lhs == rhs);
    }

    public struct Lifetime {
        public static readonly Lifetime EMPTY = new(CodePointer.EMPTY, CodePointer.EMPTY);

        public CodePointer Start { get; }
        public CodePointer? End { get; set; }

        public Lifetime(CodePointer start, CodePointer? end) {
            Start = start;
            End = end;
        }

        public Lifetime(Token startToken, Token endToken) 
                : this(new CodePointer(startToken), (endToken != null) ? new CodePointer(endToken) : null) { }

        public static Lifetime Of(List<Token> tokens) {
            if (tokens.Count == 0) {
                return EMPTY;
            } else {
                return new Lifetime(tokens.First(), tokens.LastOrDefault());
            }
        }
    }

    public class CallStack {
        public string[] Frames { get; set; }
        public int FocusFrame { get; set; }
    }

    public class FrameLocals {
        public Local[] Locals { get; set; }

        public class Local {
            public string Name { get; set; }
            public string ValueString { get; set; }
            public string ValueType { get; set; }
        }
    }

    public class DebuggerCommand {
        public sealed class Resume : DebuggerCommand { }

        public sealed class PrintHelp : DebuggerCommand { }

        public sealed class GoToFrame : DebuggerCommand {
            public int Index { get; set; }
        }

        public sealed class PrintValueOnStack : DebuggerCommand {
            public int Index { get; set; }
        }

        public sealed class StepInto : DebuggerCommand { }

        public sealed class StepOver : DebuggerCommand { }

        public sealed class StepOut : DebuggerCommand { }

        public sealed class PrintLocals : DebuggerCommand { }

        public sealed class PrintStack : DebuggerCommand { }

        public sealed class AddBreakpoint : DebuggerCommand { }

        public sealed class RemoveBreakpoint : DebuggerCommand { }
    }

    public interface IDebuggerFrontend {
        void Prepare();
        public DebuggerCommand Read();
        public void WriteMessage(string message);
        public void WriteCallStack(CallStack callStack);
        public void WriteSourceCode(string sourceCode);
        public void WriteLocals(FrameLocals locals);
        public void WriteStack(string stack);
    }

    public class ConsoleDebuggerFrontend : IDebuggerFrontend {
        public void Prepare() { }

        public DebuggerCommand Read() {
            Console.Write("nsdb ('h' for help)> ");
            var line = Console.ReadLine();
            switch (line[0]) {
                case 'h': // print help
                    return new DebuggerCommand.PrintHelp();
                case 'g': { // go to frame
                    var loc = int.Parse(line[2..]);
                    return new DebuggerCommand.GoToFrame { Index = loc };
                }
                case 'p': { // print the value at stack index
                    var loc = int.Parse(line[2..]);
                    return new DebuggerCommand.PrintValueOnStack { Index = loc };
                }
                case 'i': // step into
                    return new DebuggerCommand.StepInto();
                case 'v': // step over
                    return new DebuggerCommand.StepOver();
                case 'o': // step out
                    return new DebuggerCommand.StepOut();
                case 'l': // print all locals
                    return new DebuggerCommand.PrintLocals();
                case 's': // print the stack for the current frame
                    return new DebuggerCommand.PrintStack();
                case 'a': // add current position as breakpoint
                    return new DebuggerCommand.AddBreakpoint();
                case 'r': // remove current position as breakpoint
                    return new DebuggerCommand.RemoveBreakpoint();
                default: // reset the step status
                    return new DebuggerCommand.Resume();
            }
        }

        public void WriteMessage(string message) {
            Console.WriteLine(message);
        }

        public void WriteCallStack(CallStack callStack) {
            Console.WriteLine("CALL STACK:");
            for (var i = 0; i < callStack.Frames.Length; i++) {
                if (i == callStack.FocusFrame) {
                    Console.Write("* "); // Mark the focus frame
                }
                Console.WriteLine(callStack.Frames[i]);
            }
        }

        public void WriteLocals(FrameLocals locals) {
            Console.WriteLine("LOCALS:");
            foreach (var local in locals.Locals) {
                Console.WriteLine($"({local.ValueType}) {local.Name} = {local.ValueString}");
            }
        }

        public void WriteSourceCode(string sourceCode) {
            Console.WriteLine(sourceCode);
        }

        public void WriteStack(string stack) {
            Console.WriteLine(stack);
        }
    }
}