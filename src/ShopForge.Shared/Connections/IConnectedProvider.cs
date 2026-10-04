namespace ShopForge.Shared.Connections;

// What a payment gateway and a carrier have in common before either does any work: a name the store's methods
// refer to, and whether that name means anything until the store has connected an account to it.
//
// Both kinds of provider answer this the same way on purpose. A gateway a merchant signed up for and a carrier
// a merchant signed up for are the same situation, and the rule for whether their methods may be offered should
// not be invented twice (D-138).
public interface IConnectedProvider
{
    string Key { get; }

    // False for a provider that needs nothing from the store: methods the store settles or carries itself, and
    // the platform's own accounts, which belong to the deployment (D-059).
    bool NeedsConnection => false;
}
