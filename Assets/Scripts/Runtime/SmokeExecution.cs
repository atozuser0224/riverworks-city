using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Riverworks
{
    /// <summary>Executes nested verification routines under one exception and timeout boundary.</summary>
    public static class SmokeExecution
    {
        public static IEnumerator Run(IEnumerator steps, Action<Exception> failure, float timeoutSeconds = 300f)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(steps);
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            try
            {
                while (stack.Count > 0)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        failure(new TimeoutException("Runtime verification exceeded its deadline."));
                        yield break;
                    }
                    object next = null;
                    bool hasNext = false;
                    Exception error = null;
                    try
                    {
                        hasNext = stack.Peek().MoveNext();
                        if (hasNext) next = stack.Peek().Current;
                    }
                    catch (Exception caught) { error = caught; }
                    if (error != null)
                    {
                        failure(error);
                        yield break;
                    }
                    if (!hasNext)
                    {
                        Dispose(stack.Pop());
                        continue;
                    }
                    if (next is IEnumerator nested) stack.Push(nested);
                    else yield return next;
                }
            }
            finally
            {
                while (stack.Count > 0) Dispose(stack.Pop());
            }
        }

        static void Dispose(IEnumerator enumerator)
        {
            try { (enumerator as IDisposable)?.Dispose(); }
            catch { /* Cleanup must not hide the original verification failure. */ }
        }
    }
}
