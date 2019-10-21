using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MinecraftVersionDownloader.All
{
    public static partial class Helper
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task<JObject> GetJsonAsync(this Uri uri)
            => JObject.Parse(await uri.GetStringAsync());

        public static Task<string> GetStringAsync(this Uri uri)
            => _httpClient.GetStringAsync(uri);

        public static Task<Stream> GetStreamAsync(this Uri uri)
            => _httpClient.GetStreamAsync(uri);

        public static IEnumerable<T> Remove<T>(this IEnumerable<T> source, IEnumerable<T> toRemove)
        {
            foreach (var item in source)
                if (!toRemove.Contains(item))
                    yield return item;
        }

        public static T HeadTail<T>(this T[] array, out T[] tail)
        {
            tail = array[1..];
            return array[0];
        }

        public static T[] HeadsTail<T>(this T[] array, out T tail)
        {
            tail = array[^1];
            return array[..^1];
        }

        public static T HeadTail<T>(this IEnumerable<T> source, out IEnumerable<T> tail)
        {
            tail = source.Skip(1);
            return source.First();

            var enumerator = source.GetEnumerator();

            if (!enumerator.MoveNext())
                throw new InvalidOperationException();

            var head = enumerator.Current;

            tail = Tail(enumerator);
            return head;

            static IEnumerable<T> Tail(IEnumerator<T> source)
            {
                while (source.MoveNext())
                    yield return source.Current;
            }
        }

        public static IEnumerable<T> HeadsTail<T>(this IEnumerable<T> source, out T tail)
        {
            tail = source.Last();
            return source.SkipLast(1);
        }

        public static bool ModifyReadOnlyProperty<TThis, TProperty>(this TThis @this, Expression<Func<TThis, TProperty>> expression, in TProperty value)
            where TThis : notnull
            => ModifyReadOnlyProperty(@this, expression.Body, value);

        public static bool ModifyReadOnlyProperty<T>(Expression<Func<T>> expression, in T value)
        {
            dynamic d = expression.Compile().Target!;
            var t = (object)d.Constants[0];
            return ModifyReadOnlyProperty(t, expression.Body, value);
        }

        private static bool ModifyReadOnlyProperty<T>(object @this, Expression body, in T value)
        {
            if (body is MemberExpression prop)
                if (prop.Member is PropertyInfo propInfo)
                    if (propInfo.CanRead && !propInfo.CanWrite)
                        if (propInfo.GetMethod!.GetCustomAttribute(typeof(CompilerGeneratedAttribute)) is { })
                        {
                            var backingField = @this.GetType().GetAllFields().FirstOrDefault(field => field.Name == $@"<{propInfo.Name}>k__BackingField");
                            if (backingField is null)
                                return false;
                            backingField.SetValue(@this, value);
                            return true;
                        }
            return false;
        }

        public static IEnumerable<FieldInfo> GetAllFields(this Type t)
        {
            return GetAllFieldsImpl(t);

            static IEnumerable<FieldInfo> GetAllFieldsImpl(Type? t)
            {
                if (t == null)
                    return Enumerable.Empty<FieldInfo>();

                var flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                return t.GetFields(flags).Concat(GetAllFieldsImpl(t.BaseType));
            }
        }

        public static IEnumerable<T> IfEmpty<T>(this IEnumerable<T> source, Action empty, Action nonEmpty)
        {
            var isEmpty = true;
            
            foreach (var item in source)
            {
                isEmpty = false;
                yield return item;
            }

            if (isEmpty)
                empty();
            else
                nonEmpty();
        }
    }
}
