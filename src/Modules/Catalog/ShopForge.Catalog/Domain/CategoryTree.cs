namespace ShopForge.Catalog.Domain;

// A store's categories as a shape. Every question a hierarchy asks — what is beneath this one, what is above
// it, how deep does it sit, may this one move there — is answered here, from a list the caller has already
// read, so no page pays a query per level and the rules can be tested without a database (D-172).
internal sealed class CategoryTree
{
    // Five levels is a shop's limit, not a database's. Deeper than that is a navigation problem rather than a
    // catalogue one: a breadcrumb stops being readable, and a crawler is further from the products than it
    // needs to be.
    public const int MaxDepth = 5;

    private readonly Dictionary<Guid, Guid?> _parentOf = [];
    private readonly Dictionary<Guid, List<Guid>> _childrenOf = [];
    private readonly List<Guid> _roots = [];

    private CategoryTree(IReadOnlyList<CategoryPlace> places)
    {
        var claimed = places.ToDictionary(place => place.Id, place => place.ParentId);

        // Whatever arrives is turned into a genuine forest first: a parent nothing in this store answers for,
        // and a parent that is reached by walking up from itself, both become no parent at all. Only
        // `CanAdopt` can write a cycle and it refuses to, so this is for rows something else has broken —
        // and it means every walk below ends without needing a guard of its own.
        foreach (var (id, claimedParent) in claimed)
        {
            _parentOf[id] = Rooted(claimed, id, claimedParent) ? null : claimedParent;
        }

        foreach (var place in places)
        {
            if (_parentOf[place.Id] is { } parentId)
            {
                if (!_childrenOf.TryGetValue(parentId, out var siblings))
                {
                    _childrenOf[parentId] = siblings = [];
                }

                siblings.Add(place.Id);
            }
            else
            {
                _roots.Add(place.Id);
            }
        }
    }

    private static bool Rooted(Dictionary<Guid, Guid?> claimed, Guid id, Guid? claimedParent)
    {
        if (claimedParent is not { } parent || !claimed.ContainsKey(parent))
        {
            return true;
        }

        var walked = parent;

        for (var step = 0; step < claimed.Count; step++)
        {
            if (walked == id)
            {
                return true;
            }

            if (claimed.GetValueOrDefault(walked) is not { } next || !claimed.ContainsKey(next))
            {
                return false;
            }

            walked = next;
        }

        return true;
    }

    // The order the caller supplied is the order children keep, so sorting belongs to whoever read the rows.
    public static CategoryTree Of(IReadOnlyList<CategoryPlace> places) => new(places);

    public IReadOnlyList<Guid> Roots => _roots;

    public IReadOnlyList<Guid> ChildrenOf(Guid id) => _childrenOf.TryGetValue(id, out var children) ? children : [];

    public bool Knows(Guid id) => _parentOf.ContainsKey(id);

    // Parents before their children, so a flat list can be drawn as a tree in one pass.
    public IReadOnlyList<Guid> Everything()
    {
        var ordered = new List<Guid>(_parentOf.Count);

        void Walk(Guid id)
        {
            ordered.Add(id);

            foreach (var child in ChildrenOf(id))
            {
                Walk(child);
            }
        }

        foreach (var root in _roots)
        {
            Walk(root);
        }

        return ordered;
    }

    // This one and everything beneath it: what a parent category sells (D-146), each category named once
    // however the tree is shaped.
    public IReadOnlyList<Guid> AndBeneath(Guid id)
    {
        if (!Knows(id))
        {
            return [];
        }

        var found = new List<Guid> { id };

        for (var index = 0; index < found.Count; index++)
        {
            found.AddRange(ChildrenOf(found[index]));
        }

        return found;
    }

    // Root first, this one last: a breadcrumb read left to right.
    public IReadOnlyList<Guid> PathTo(Guid id)
    {
        if (!Knows(id))
        {
            return [];
        }

        var path = new List<Guid>();

        for (var walked = id; ; walked = _parentOf[walked]!.Value)
        {
            path.Add(walked);

            if (_parentOf[walked] is null)
            {
                break;
            }
        }

        path.Reverse();

        return path;
    }

    public int DepthOf(Guid id) => PathTo(id).Count;

    // How many levels this one and its deepest descendant span, counting itself as one.
    public int HeightOf(Guid id) => ChildrenOf(id) is { Count: > 0 } children ? 1 + children.Max(HeightOf) : 1;

    // Whether this one may sit under that one. Three ways it may not: a category cannot be its own parent,
    // cannot move beneath something it already contains, and cannot push its own deepest child past the limit
    // — the case that is easy to forget, because moving a shallow branch under a deep one breaks a rule
    // neither end breaks alone.
    public bool CanAdopt(Guid id, Guid? parentId)
    {
        if (parentId is null)
        {
            return true;
        }

        if (parentId == id || !Knows(parentId.Value) || !Knows(id) || AndBeneath(id).Contains(parentId.Value))
        {
            return false;
        }

        return DepthOf(parentId.Value) + HeightOf(id) <= MaxDepth;
    }

    public bool CanHoldAChild(Guid parentId) => Knows(parentId) && DepthOf(parentId) < MaxDepth;
}

internal readonly record struct CategoryPlace(Guid Id, Guid? ParentId);
