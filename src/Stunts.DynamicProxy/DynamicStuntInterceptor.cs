﻿using System;
using System.Collections.Generic;
using Castle.DynamicProxy;

namespace Stunts
{
    internal class DynamicStuntInterceptor : IInterceptor, IStunt // Implemented to detect breaking changes in Stunts
    {
        readonly bool notImplemented;
        readonly BehaviorPipeline pipeline;

        internal DynamicStuntInterceptor(bool notImplemented)
        {
            this.notImplemented = notImplemented;
            pipeline = new BehaviorPipeline();
        }

        public IList<IStuntBehavior> Behaviors => pipeline.Behaviors;

        public virtual void Intercept(IInvocation invocation)
        {
            if (invocation.Method.DeclaringType == typeof(IStunt))
            {
                invocation.ReturnValue = Behaviors;
                return;
            }

            var input = new MethodInvocation(invocation.Proxy, invocation.Method, invocation.Arguments);
            var returns = pipeline.Invoke(input, (i, next) => {
                try
                {
                    if (notImplemented)
                        throw new NotImplementedException();

                    invocation.Proceed();
                    var returnValue = invocation.ReturnValue;
                    return input.CreateValueReturn(returnValue, invocation.Arguments);
                }
                catch (Exception ex)
                {
                    return input.CreateExceptionReturn(ex);
                }
            });

            var exception = returns.Exception;
            if (exception != null)
                throw exception;

            invocation.ReturnValue = returns.ReturnValue;
            for (var i = 0; i < returns.Outputs.Count; i++)
            {
                var name = returns.Outputs.GetInfo(i).Name;
                var index = input.Arguments.IndexOf(name);
                invocation.SetArgumentValue(index, returns.Outputs[i]);
            }
        }
    }
}
