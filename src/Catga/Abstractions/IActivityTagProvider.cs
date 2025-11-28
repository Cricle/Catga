using System.Diagnostics;

namespace Catga.Abstractions;

public interface IActivityTagProvider
{
    void Enrich(Activity activity);
}
