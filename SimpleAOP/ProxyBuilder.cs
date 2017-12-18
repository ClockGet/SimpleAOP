using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace SimpleAOP
{
    internal class ProxyBuilder
    {
        private IList<Func<InvokeHandlerDelegate, InvokeHandlerDelegate>> delegateList = new List<Func<InvokeHandlerDelegate, InvokeHandlerDelegate>>();
        private InvokeHandlerDelegate _last;
        public ProxyBuilder(InvokeHandlerDelegate last)
        {
            _last = last;
        }

        /*
         * .Lambda #Lambda1<ConsoleApplication15.InvokeHandlerDelegate>(ConsoleApplication15.IMethodInvocation $input) {
         *      .Block(
         *          params $var) {
         *          $varN = (ParameterType).Call ($input.Arguments).get_Item(N);
         *          .Try {
         *              .Call $input.CreateMethodReturn(
         *                  (System.Object).Call ((methodInfo.DeclaringType)$input.Target).$method(
         *                      params $var),
         *                  .NewArray System.Object[] {
         *                      params (System.Object)$var
         *                  })
         *          } .Catch (System.Exception $exception) {
         *              .Call $input.CreateExceptionMethodReturn($exception)
         *          }
         *      }
         *  }
         */
        private static Delegate FixCall(MethodInfo method)
        {
            var parameterTypes = new Type[] { method.DeclaringType }.Concat(method.GetParameters().Select(p => p.ParameterType)).ToArray();
            DynamicMethod dm = new DynamicMethod($"__DynamicMethod__{method.Name}", MethodAttributes.Public | MethodAttributes.Static, CallingConventions.Standard, method.ReturnType, parameterTypes, method.DeclaringType, false);
            var il = dm.GetILGenerator();
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldarg, i);
            }
            il.Emit(OpCodes.Call, method);
            il.Emit(OpCodes.Ret);
            Type delegateType = Expression.GetDelegateType(parameterTypes.Concat(method.ReturnType != typeof(void) ? new Type[] { method.ReturnType } : new Type[] { typeof(void) }).ToArray());
            return dm.CreateDelegate(delegateType);
        }
        public static InvokeHandlerDelegate ConvertToDelegate(MethodInfo methodInfo)
        {
            var p = Expression.Parameter(typeof(IMethodInvocation), "input");
            var arguments = Expression.Property(p, typeof(IMethodInvocation).GetProperty("Arguments"));
            var instance = Expression.Property(p, typeof(IMethodInvocation).GetProperty("Target"));
            var index = 0;
            var fixCallMethod = FixCall(methodInfo);
            List<ParameterExpression> methodParameters = new List<ParameterExpression>();
            ParameterExpression v;
            List<Expression> localLogic = new List<Expression>();
            foreach (var parameterInfo in methodInfo.GetParameters())
            {
                var type = parameterInfo.ParameterType;
                if (type.IsByRef)
                {
                    type = type.GetElementType();
                }
                v = Expression.Parameter(type);
                localLogic.Add(Expression.Assign(v, Expression.Convert(Expression.Call(arguments, typeof(IParameterCollection).GetMethod("get_Item", new Type[] { typeof(int) }), Expression.Constant(index, typeof(int))), v.Type)));
                methodParameters.Add(v);
                index++;
            }

            var exception = Expression.Parameter(typeof(Exception), "exception");
            TryExpression tryCatchBody;
            if (methodInfo.ReturnType != typeof(void))
            {
                tryCatchBody = Expression.TryCatch(
                Expression.Call(
                    p,
                    typeof(IMethodInvocation).GetMethod("CreateMethodReturn"),
                    new Expression[]
                    {
                        Expression.Convert(Expression.Invoke(Expression.Constant(fixCallMethod),new Expression[] {Expression.Convert(instance,methodInfo.DeclaringType) }.Concat(methodParameters).ToArray()),typeof(object)),
                        //Expression.Call(null, fixCallMethod.Method,new Expression[] {Expression.Convert(instance,methodInfo.DeclaringType) }.Concat(methodParameters).ToArray()),
                        //Expression.Convert(Expression.Call(Expression.Convert(instance, methodInfo.DeclaringType), methodInfo, methodParameters.ToArray()),typeof(object)),
                        Expression.NewArrayInit(typeof(object),methodParameters.Select(mp=>Expression.Convert(mp,typeof(object))))
                    }),
                Expression.Catch(exception, Expression.Call(p, typeof(IMethodInvocation).GetMethod("CreateExceptionMethodReturn"), exception))
                );
            }
            else
            {
                tryCatchBody = Expression.TryCatch(
                    Expression.Block(
                        Expression.Invoke(Expression.Constant(fixCallMethod), new Expression[] { Expression.Convert(instance, methodInfo.DeclaringType) }.Concat(methodParameters).ToArray()),
                        //Expression.Call(null, fixCallMethod.Method,new Expression[] {Expression.Convert(instance,methodInfo.DeclaringType) }.Concat(methodParameters).ToArray()),
                        //Expression.Call(Expression.Convert(instance, methodInfo.DeclaringType), methodInfo, methodParameters.ToArray()),
                        Expression.Call(
                            p,
                            typeof(IMethodInvocation).GetMethod("CreateMethodReturn"),
                            new Expression[]
                            {
                                Expression.Constant(null),
                                Expression.NewArrayInit(typeof(object),methodParameters.Select(mp=>Expression.Convert(mp,typeof(object))))
                            })
                        ),
                    Expression.Catch(exception, Expression.Call(p, typeof(IMethodInvocation).GetMethod("CreateExceptionMethodReturn"), exception))
                );
            }
            localLogic.Add(tryCatchBody);
            var body = Expression.Block(methodParameters, localLogic);
            var lambda = Expression.Lambda<InvokeHandlerDelegate>(body, p);
            return lambda.Compile();
        }
        public ProxyBuilder Use(Func<IMethodInvocation, InvokeHandlerDelegate, IMethodReturn> fun)
        {
            Func<InvokeHandlerDelegate, InvokeHandlerDelegate> func = (next) =>
             {
                 return new InvokeHandlerDelegate(input =>
                 {
                     return fun(input, next);
                 });
             };
            delegateList.Add(func);
            return this;
        }
        public InvokeHandlerDelegate Build()
        {
            InvokeHandlerDelegate @delegate = _last;
            foreach (var d in delegateList.Reverse())
            {
                @delegate = d(@delegate);
            }
            return @delegate;
        }
    }
}
