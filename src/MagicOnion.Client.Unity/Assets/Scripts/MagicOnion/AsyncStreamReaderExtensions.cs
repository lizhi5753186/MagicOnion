﻿using Grpc.Core;
using System;
using UniRx;

namespace MagicOnion
{
    public static class AsyncStreamReaderExtensions
    {
        public static IObservable<Unit> ForEachAsync<T>(this IAsyncStreamReader<T> stream, Action<T> action)
        {
            return RecursiveActionAsync(stream, action);
        }

        static IObservable<Unit> RecursiveActionAsync<T>(IAsyncStreamReader<T> stream, Action<T> action)
        {
            return stream.MoveNext().ContinueWith(x =>
            {
                if (x)
                {
                    action(stream.Current);
                    return RecursiveActionAsync(stream, action);
                }
                else
                {
                    stream.Dispose();
                    return Observable.ReturnUnit();
                }
            });
        }

        public static IObservable<Unit> ForEachAsync<T>(this IAsyncStreamReader<T> stream, Func<T, IObservable<Unit>> asyncAction)
        {
            return RecursiveActionAsync(stream, asyncAction);
        }

        static IObservable<Unit> RecursiveActionAsync<T>(IAsyncStreamReader<T> stream, Func<T, IObservable<Unit>> asyncAction)
        {
            return stream.MoveNext().ContinueWith(x =>
            {
                if (x)
                {
                    return asyncAction(stream.Current)
                        .ContinueWith(_ => RecursiveActionAsync(stream, asyncAction));
                }
                else
                {
                    stream.Dispose();
                    return Observable.ReturnUnit();
                }
            });
        }

        public static IObservable<T> AsObservable<T>(this IAsyncStreamReader<T> stream, bool observeOnMainThread = true)
        {
            var seq = Observable.Create<T>(observer =>
            {
                return stream.ForEachAsync(x => observer.OnNext(x)).Subscribe(_ => { }, observer.OnError, observer.OnCompleted);
            });

            return (observeOnMainThread) ? seq.ObserveOnMainThread() : seq;
        }
    }
}
