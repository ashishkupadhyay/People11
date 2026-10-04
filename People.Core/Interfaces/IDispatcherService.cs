using System;

namespace People.Core.Interfaces;

public interface IDispatcherService
{
    void Enqueue(Action action);
}
