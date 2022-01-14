using Fluxor;
using NNostr.UI.Services;

namespace NNostr.UI.Store;

[FeatureState]
public record UsersState
{
    public LoadState State { get; } = LoadState.NotLoaded;
    public Dictionary<string, string>? Users { get; }

    public UsersState()
    {
        
    }

    public UsersState(LoadState State, Dictionary<string, string>? Users)
    {
        this.State = State;
        this.Users = Users;
    }
}

public record LoadUsersAction();
public record UnLoadUsersAction();
public record LoadedUsersAction(UsersState State);

public class UnLoadUsersActionReducer:Reducer<UsersState, UnLoadUsersAction>
{
    public override UsersState Reduce(UsersState state, UnLoadUsersAction action)
    {
        return new UsersState(LoadState.NotLoaded, null);
    }
}
public class LoadUsersActionReducer:Reducer<UsersState, LoadUsersAction>
{
    public override UsersState Reduce(UsersState state, LoadUsersAction action)
    {
        return new UsersState(LoadState.Loading, null);
    }
}
public class LoadedUsersActionReducer:Reducer<UsersState, LoadedUsersAction>
{
    public override UsersState Reduce(UsersState state, LoadedUsersAction action)
    {
        return action.State;
    }
}
public class LoadUsersActionEffect:Effect<LoadUsersAction>
{
    private readonly UserRepository _userRepository;

    public LoadUsersActionEffect(UserRepository userRepository)
    {
        _userRepository = userRepository;
    }
    public override async Task HandleAsync(LoadUsersAction action, IDispatcher dispatcher)
    {
        try
        {
            var users = await _userRepository.GetAvailableUsers();
            dispatcher.Dispatch(new LoadedUsersAction(new UsersState(LoadState.Loaded, users)));
        }
        catch (Exception e)
        {
            dispatcher.Dispatch(new LoadedUsersAction(new UsersState(LoadState.Error, null)));
        }
    }
}