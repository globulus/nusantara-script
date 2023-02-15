using NusantaraScript;
using System;

namespace nusantara_script
{
    class Program {
        static void Main(string[] args) {
           TestScripting();
        }

        private static void TestScripting() {
            var fileName = "test.ns";
            var driver = new ScriptDriver();
            var vm = driver.BootVm(true, (e) => Console.WriteLine($"ERROR: {e}"), fileName);
        }
    }
}
