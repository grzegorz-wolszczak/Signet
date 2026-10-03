using CommunityToolkit.Mvvm.ComponentModel;

namespace Signet.App.ViewModels;

/// <summary>
/// The common base for all view models. Provides the
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/> implementation from CommunityToolkit.Mvvm.
/// </summary>
public abstract class ViewModelBase : ObservableObject;
