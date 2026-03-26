namespace McpHost;

public sealed class McpRegistry
{
    private readonly Dictionary<string, RegisteredTool> _tools = new(StringComparer.Ordinal);

    public void Extend(IEnumerable<RegisteredTool> tools)
    {
        foreach (var tool in tools)
        {
            if (_tools.TryGetValue(tool.Name, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate tool name '{tool.Name}' exposed by both '{existing.ServerAlias}' and '{tool.ServerAlias}'.");
            }

            _tools[tool.Name] = tool;
        }
    }

    public RegisteredTool? Get(string name) => _tools.GetValueOrDefault(name);

    public void Clear() => _tools.Clear();

    public int Count => _tools.Count;
}
