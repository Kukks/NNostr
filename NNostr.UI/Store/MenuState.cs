using Fluxor;

namespace NNostr.UI.Store;

public record MenuItem(string Link, string? Icon, string Text);

public record AddMenuItemAction(MenuItem[] MenuItems);
public record RemoveMenuItemAction(string[] Links);

[FeatureState]
public record MenuState()
{
    public MenuState(List<MenuItem> menuItems):this()
    {
        MenuItems = menuItems;
    }
    public List<MenuItem> MenuItems { get; } = new List<MenuItem>();
}

public class AddMenuItemReducer : Reducer<MenuState, AddMenuItemAction>
{
    public override MenuState Reduce(MenuState state, AddMenuItemAction action)
    {
        foreach (var menuItem in action.MenuItems)
        {
            if (!state.MenuItems.Contains(menuItem))
            {
                state.MenuItems.Add(menuItem);
            }
        }
        
        return new MenuState(new List<MenuItem>(state.MenuItems));
    }
}

public class RemoveMenuItemActionReducer : Reducer<MenuState, RemoveMenuItemAction>
{
    public override MenuState Reduce(MenuState state, RemoveMenuItemAction action)
    {

        state.MenuItems.RemoveAll(item => action.Links.Contains(item.Link));

        return new MenuState(new List<MenuItem>(state.MenuItems)); ;
    }
}