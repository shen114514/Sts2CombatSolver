using System.Text;

namespace CombatSolver.Engine.InCombat.Simulation;

internal enum BranchArrowStatus { Retrieved, Away, Destroyed }

internal sealed class ArrowBranchState
{
    internal sealed record Slot(BranchArrowStatus Status, bool DestroyAfterImpaled);
    private readonly List<Slot> _slots;
    private readonly SortedDictionary<uint, List<int>> _targets;
    private readonly Dictionary<uint, int> _hits;
    public bool YahTriggered { get; set; }

    public ArrowBranchState(IEnumerable<Slot> slots)
    {
        _slots = slots.ToList(); _targets = new(); _hits = new();
    }
    private ArrowBranchState(ArrowBranchState source)
    {
        _slots = source._slots.ToList();
        _targets = new(source._targets.ToDictionary(p => p.Key, p => p.Value.ToList()));
        _hits = new(source._hits);
        YahTriggered = source.YahTriggered;
    }
    public ArrowBranchState Fork() => new(this);
    public int Count => _slots.Count;
    public int AvailableCount => _slots.Count(s => s.Status == BranchArrowStatus.Retrieved);
    public Slot this[int index] => _slots[index];
    public IReadOnlyList<int> ArrowsOn(uint target) => _targets.TryGetValue(target, out var arrows) ? arrows : Array.Empty<int>();
    public int HitCounter(uint target) => _hits.GetValueOrDefault(target, 3);
    public void GainSlot() => _slots.Add(new(BranchArrowStatus.Retrieved, false));
    public int TryShoot()
    {
        for (int index = _slots.Count - 1; index >= 0; index--)
            if (_slots[index].Status == BranchArrowStatus.Retrieved)
            {
                _slots[index] = _slots[index] with { Status = BranchArrowStatus.Away };
                return index;
            }
        return -1;
    }
    public void MarkForDestruction(int index) => _slots[index] = _slots[index] with { DestroyAfterImpaled = true };
    public void Impale(uint target, int index, int? capturedHits = null)
    {
        if (_slots[index].Status != BranchArrowStatus.Away || _targets.Values.Any(v => v.Contains(index)))
            throw new InvalidOperationException("Arrow is not available for impaling");
        if (!_targets.TryGetValue(target, out var arrows))
        {
            _targets[target] = arrows = new(); _hits[target] = capturedHits ?? 3;
        }
        arrows.Add(index);
    }
    // One callback per powered hit, including fully blocked/zero-damage hits, matching vanilla.
    public bool RegisterHit(uint target)
    {
        if (!_targets.ContainsKey(target)) return false;
        int remaining = _hits[target] - 1;
        _hits[target] = remaining <= 0 ? 3 : remaining;
        return remaining <= 0;
    }
    public int DetachFirst(uint target)
    {
        if (!_targets.TryGetValue(target, out var arrows) || arrows.Count == 0) return -1;
        int index = arrows[0]; arrows.RemoveAt(0);
        if (arrows.Count == 0) { _targets.Remove(target); _hits.Remove(target); }
        return index;
    }
    public void Retrieve(int index)
    {
        if (_slots[index].Status != BranchArrowStatus.Away)
            throw new InvalidOperationException("Only away arrows can be retrieved");
        _slots[index] = new(BranchArrowStatus.Retrieved, false);
    }
    public void Destroy(int index)
    {
        foreach (uint target in _targets.Keys.ToArray())
        {
            _targets[target].Remove(index);
            if (_targets[target].Count == 0) { _targets.Remove(target); _hits.Remove(target); }
        }
        _slots[index] = _slots[index] with { Status = BranchArrowStatus.Destroyed };
    }
    public string StateText()
    {
        StringBuilder text = new();
        for (int i = 0; i < _slots.Count; i++)
            text.Append(i).Append(':').Append((int)_slots[i].Status).Append(':').Append(_slots[i].DestroyAfterImpaled ? 1 : 0).Append(',');
        text.Append('|');
        foreach (var pair in _targets)
            text.Append(pair.Key).Append(':').Append(_hits[pair.Key]).Append(':').AppendJoin(',', pair.Value).Append('/');
        return text.ToString();
    }
}
