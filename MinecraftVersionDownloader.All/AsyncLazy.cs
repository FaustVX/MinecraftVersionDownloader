﻿using System;
using System.Threading.Tasks;

namespace MinecraftVersionDownloader.All
{
    public class AsyncLazy<T> : Lazy<Task<T>>
    {
        public AsyncLazy(Func<T> valueFactory) :
            base(() => Task.Factory.StartNew(valueFactory))
        { }

        public AsyncLazy(Func<Task<T>> taskFactory) :
            base(() => Task.Factory.StartNew(() => taskFactory()).Unwrap())
        { }

        public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter()
            => Value.GetAwaiter();
    }
}
