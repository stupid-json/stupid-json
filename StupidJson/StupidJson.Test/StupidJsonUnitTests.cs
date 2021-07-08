using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VerifyCS = StupidJson.Test.CSharpCodeFixVerifier<
    StupidJson.StupidJsonAnalyzer,
    StupidJson.StupidJsonCodeFixProvider>;

namespace StupidJson.Test
{
    [TestClass]
    public class StupidJsonUnitTest
    {
        [TestMethod]
        public async Task StupidJsonAnnotationTest()
        {
            var test = @"
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Diagnostics;

    namespace TestApplication
    {
        [StupidJson]
        class SomeDtoClass
        {   
            public SomeDtoClass(int value) {}

            public string Name { get; set; }

            public Dictionary<string, object> Stuff { get; set; }

            public int Age { get; set; }

            public void PrintLine() {
                Console.WriteLine();
            }

        }
    }";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task Test2()
        {
            var test = @"
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Diagnostics;

    namespace TestApplication
    {
        [StupidJson]
        class SomeDtoClass
        {   
            public SomeDtoClass(int value) {}

            public string Name { get; set; }

            public Dictionary<string, object> Stuff { get; set; }

            public int Age { get; set; }

            public object Dees() {
                return JsonConvert.DeserializeObject<SomeDtoClass>(this);
            }

        }
    }";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task Test3()
        {
            var test = @"
using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace NewtonsoftDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            //Console.WriteLine(""Hello World!"");
            var s = ""Hello"";
            var d1 = JsonConvert.DeserializeObject<StupidJsonDocument>(s);
            //var d2 = JsonConvert.DeserializeObject<JsonDocument>(s);
        }
    }

    [StupidJson]
    class StupidJsonDocument
    {
        public StupidJsonDocument(int val) { }
        public DateTime Timestamp { get; set; }
        public string Text { get; set; }
        public int Value { get; set; }
        public double AnotherValue { get; set; }
        public bool Flag { get; set; }
        public bool? MaybeFlag { get; set; }
        public Status Status { get; set; }
        public Status? MaybeStatus { get; set; }
        public Dictionary<object, object> Dict1 { get; set; }
        public Dictionary<string, object> Dict2 { get; set; }
        public string[] Array { get; set; }
        public string[][] NestedArray { get; set; }
        public Status[] StatusArray { get; set; }
        public Status[][] NestedStatusArray { get; set; }
        public IEnumerable<string> Seq { get; set; }
        public List<string> List { get; set; }
        public List<Status> StatusList { get; set; }
    }

    enum Status { Undefined, Maybe, Definitely }

    class JsonDocument
    {
        public JsonDocument(int val) { }
    }

    public class StupidJsonAttribute : Attribute { }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task Test4()
        {
            var test = @"
using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace NewtonsoftDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            //Console.WriteLine(""Hello World!"");
            var s = ""Hello"";
            var d2 = JsonConvert.DeserializeObject<JsonDocument>(s);
        }
    }

    class JsonDocument
    {
        public JsonDocument(int val) { }

        public DateTime Timestamp { get; set; }
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

    }
}
