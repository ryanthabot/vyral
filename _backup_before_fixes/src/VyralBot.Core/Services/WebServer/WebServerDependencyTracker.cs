namespace VyralBot.Core.Services.WebServer;

public class WebServerDependencyTracker
{
    private readonly Dictionary<string, HashSet<string>> _dependencies = new();
    private readonly object _lock = new();

    public void RegisterDependency(string featureName, string reason)
    {
        lock (_lock)
        {
            if (!_dependencies.ContainsKey(featureName))
            {
                _dependencies[featureName] = new HashSet<string>();
            }

            _dependencies[featureName].Add(reason);
        }
    }

    public void UnregisterDependency(string featureName, string reason)
    {
        lock (_lock)
        {
            if (_dependencies.ContainsKey(featureName))
            {
                _dependencies[featureName].Remove(reason);

                if (_dependencies[featureName].Count == 0)
                {
                    _dependencies.Remove(featureName);
                }
            }
        }
    }

    public bool CanDisableWebServer()
    {
        lock (_lock)
        {
            return _dependencies.Count == 0;
        }
    }

    public List<string> GetActiveDependencies()
    {
        lock (_lock)
        {
            var activeDependencies = new List<string>();

            foreach (var feature in _dependencies)
            {
                foreach (var reason in feature.Value)
                {
                    activeDependencies.Add($"{feature.Key}: {reason}");
                }
            }

            return activeDependencies;
        }
    }

    public bool HasDependencies(string featureName)
    {
        lock (_lock)
        {
            return _dependencies.ContainsKey(featureName) && _dependencies[featureName].Count > 0;
        }
    }

    public List<string> GetFeatureDependencies(string featureName)
    {
        lock (_lock)
        {
            if (_dependencies.ContainsKey(featureName))
            {
                return _dependencies[featureName].ToList();
            }

            return new List<string>();
        }
    }

    public void ClearAllDependencies()
    {
        lock (_lock)
        {
            _dependencies.Clear();
        }
    }

    public Dictionary<string, List<string>> GetAllDependenciesGrouped()
    {
        lock (_lock)
        {
            return _dependencies.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList()
            );
        }
    }
}
