using BlazingPay.WebCommon;
using Fluxor;
using NNostr.UI.Services;

namespace NNostr.UI.Store;
[FeatureState]
public record CurrentUserState()
{
    public CurrentUserState(LoadState state, User? user, string? passphrase) : this()
    {
        State = state;
        User = user;
        Passphrase = passphrase;
    }

    public LoadState State { get; set; } = LoadState.NotLoaded;
    public string? Passphrase { get; set; }
    public User? User { get; set; }
}

public record LoadCurrentUserAction();

public record LoadedCurrentUserAction(CurrentUserState State);

public class LoadCurrentUserActionReducer : Reducer<CurrentUserState, LoadCurrentUserAction>
{
    public override CurrentUserState Reduce(CurrentUserState state, LoadCurrentUserAction action)
    {
        return new CurrentUserState(LoadState.Loading, null, null);
    }
}

public class LoadedCurrentUserActionReducer : Reducer<CurrentUserState, LoadedCurrentUserAction>
{
    public override CurrentUserState Reduce(CurrentUserState state, LoadedCurrentUserAction action)
    {
        return action.State;
    }
}

public class LoadedCurrentUserActionEffect : Effect<LoadedCurrentUserAction>
{

    public override  async Task HandleAsync(LoadedCurrentUserAction action, IDispatcher dispatcher)
    {
        if (action.State.State == LoadState.Loaded)
        {

            if (action.State.User is null)
            {
                dispatcher.Dispatch(new RemoveMenuItemAction( new []{ "/profile", "/timeline", "select-user" }));
               
            }
            else
            {
                dispatcher.Dispatch(new RemoveMenuItemAction(new []{ "/profile" }));
                dispatcher.Dispatch(new AddMenuItemAction(new []{ new MenuItem("/profile", "person", action.State.User.Username ?? action.State.User.Key) , new MenuItem("/timeline", "time", "Timeline") , new MenuItem("/select-user", null, "Switch Identity") }));
            }
        }
    }
}

public class LoadCurrentUserActionEffect : Effect<LoadCurrentUserAction>
{
    private readonly SessionStorageService _sessionStorageService;
    private readonly UserRepository _userRepository;

    public LoadCurrentUserActionEffect(SessionStorageService sessionStorageService, UserRepository userRepository)
    {
        _sessionStorageService = sessionStorageService;
        _userRepository = userRepository;
    }

    public override async Task HandleAsync(LoadCurrentUserAction action, IDispatcher dispatcher)
    {
        var userId = await _sessionStorageService.Get<string?>("currentuser");
        var passphrase = await _sessionStorageService.Get<string?>("currentuser_passphrase");
        User? user = null;
        if (userId is not null)
        {
            try
            {
                user = await _userRepository.GetUser(userId, passphrase);
            }
            catch (Exception e)
            {
                dispatcher.Dispatch(
                    new LoadedCurrentUserAction(new CurrentUserState(LoadState.Error, null, passphrase)));
            }
        }

        dispatcher.Dispatch(new LoadedCurrentUserAction(new CurrentUserState(LoadState.Loaded, user, passphrase)));
    }
}

public record SetCurrentUserAction(string UserId, string? Passphrase);

public class SetCurrentUserActionReducer : Reducer<CurrentUserState, SetCurrentUserAction>
{
    public override CurrentUserState Reduce(CurrentUserState state, SetCurrentUserAction action)
    {
        return new CurrentUserState(LoadState.Loading, null, action.Passphrase);
    }
}

public class SetCurrentUserActionEffect : Effect<SetCurrentUserAction>
{
    private readonly SessionStorageService _sessionStorageService;
    private readonly UserRepository _userRepository;

    public SetCurrentUserActionEffect(SessionStorageService sessionStorageService, UserRepository userRepository)
    {
        _sessionStorageService = sessionStorageService;
        _userRepository = userRepository;
    }

    public override async Task HandleAsync(SetCurrentUserAction action, IDispatcher dispatcher)
    {
        var user = await _userRepository.GetUser(action.UserId, action.Passphrase);
        if (user is not null)
        {
            dispatcher.Dispatch(
                new LoadedCurrentUserAction(new CurrentUserState(LoadState.Loaded, user, action.Passphrase)));

            await _sessionStorageService.Set("currentuser", action.UserId);
            await _sessionStorageService.Set("currentuser_passphrase", action.Passphrase);
        }
    }
}