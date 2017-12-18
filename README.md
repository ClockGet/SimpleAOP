# SimpleAOP

## Test Sample

```csharp

	public abstract class ITest
    {
        [LoggerHandler(Order =1)]
        public abstract void Talk(string content);
        [LoggerHandler(Order =1)]
        public abstract int Calc(int a, int b);
    }
    public class Test:ITest
    {
        public override void Talk(string content)
        {
            Console.WriteLine($"hello {content}");
        }
        public override int Calc(int a, int b)
        {
            return a + b;
        }
    }
    public class LoggerHandlerAttribute:HandlerAttribute
    {
        public override ICallHandler CreateHandler()
        {
            return new LoggerHandler { Order = Order };
        }
    }
    public class LoggerHandler : ICallHandler
    {
        public int Order
        {
            get;

            set;
        }

        public IMethodReturn Invoke(IMethodInvocation input, InvokeHandlerDelegate next)
        {
            Console.WriteLine("method start");
            var r= next(input);
            Console.WriteLine("method end");
            return r;
        }
    }
    class Program
    {
        static void Main(string[] args)
        {
            AOPContainer container = new AOPContainer();
            container.Register<Test>();
            var t = container.Resolve<Test>();
            t.Talk("ys");
            var r = t.Calc(12, 13);
            Console.WriteLine($"The result of calling method Calc is {r}");
        }
    }
```