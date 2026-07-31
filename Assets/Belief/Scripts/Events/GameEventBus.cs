using System;
using System.Collections.Generic;

namespace Belief.Events
{
    /// <summary>
    /// 전달 전용 이벤트 버스. 상태를 갖지 않고, 상태 변경도 수행하지 않는다 -
    /// 구독자가 알아서 자신의 책임 범위 안에서 상태를 바꾼다.
    /// </summary>
    public interface IGameEventBus
    {
        void Subscribe<T>(Action<T> handler);
        void Unsubscribe<T>(Action<T> handler);
        void Publish<T>(T evt);
    }

    public class GameEventBus : IGameEventBus
    {
        readonly Dictionary<Type, Delegate> handlers = new Dictionary<Type, Delegate>();

        public void Subscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            handlers[type] = handlers.TryGetValue(type, out var existing)
                ? Delegate.Combine(existing, handler)
                : handler;
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (!handlers.TryGetValue(type, out var existing)) return;

            var result = Delegate.Remove(existing, handler);
            if (result == null) handlers.Remove(type);
            else handlers[type] = result;
        }

        public void Publish<T>(T evt)
        {
            if (handlers.TryGetValue(typeof(T), out var existing) && existing is Action<T> action)
                action.Invoke(evt);
        }
    }
}
