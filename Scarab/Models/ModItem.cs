using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PropertyChanged.SourceGenerator;
using Scarab.Interfaces;
using Scarab.Services;
using Scarab.Util;

namespace Scarab.Models
{
    public partial class ModItem : INotifyPropertyChanged, IEquatable<ModItem>
    {
        public ModItem
        (
            ModState state,
            Version version,
            string[] dependencies,
            string link,
            string shasum,
            string name,
            string description,
            string repository,
            string[] tags,
            string[] integrations,
            string[] authors
        )
        {
            _state = state;

            Sha256 = shasum;
            Version = version;
            Dependencies = dependencies;
            Link = link;
            Name = name;
            Description = description;
            Repository = repository;
            Tags = tags;
            Integrations = integrations;
            Authors = authors;

            DependenciesDesc = string.Join(", ", Dependencies);
            TagDesc          = string.Join(", ", Tags);
            IntegrationsDesc = string.Join(", ", Integrations);
            AuthorsDesc      = string.Join(", ", Authors);
            ShortenedRepository = Repository;
            
            try
            {
                // only remove http part if its a github link
                if (ShortenedRepository.Contains("github.com"))
                {
                    var path = new Uri(Repository).AbsolutePath;
                    ShortenedRepository = path.TrimStart('/').TrimEnd('/');
                }
            }
            catch (InvalidOperationException e)
            {
                Trace.Write($"Unable to get absolute path of {Repository} {e}");
            }
        }


        public Version  Version          { get; }
        public string[] Dependencies     { get; }
        public string   Link             { get; }
        public string   Sha256           { get; }
        public string   Name             { get; }
        public string   Description      { get; }
        public string   Repository       { get; }
        
        public string[] Tags { get; }
        public string[] Integrations { get; }
        public string[] Authors { get; }
        
        public string   ShortenedRepository   { get; }
        public string   DependenciesDesc { get; }
        public string   TagDesc          { get; }
        public string   IntegrationsDesc { get; }
        
        public string AuthorsDesc { get; }

        [Notify]
        private ModState _state;

        public bool EnabledIsChecked => State switch
        {
            InstalledState { Enabled: var x } => x,
            NotInModLinksState {Enabled: var x} => x,
            // Can't enable what isn't installed.
            _ => false
        };

        public bool InstallingButtonAccessible => State is NotInstalledState { Installing: true } || DisplayErrors.IsLoadingAnError;

        public string InstallText => State switch
        {
            InstalledState => Resources.MI_InstallText_Installed,
            NotInstalledState => Resources.MI_InstallText_NotInstalled,
            NotInModLinksState => Resources.MI_InstallText_NotInModlinks,
            _ => throw new InvalidOperationException("Unreachable")
        };

        public bool Installed => State is InstalledState or NotInModLinksState;

        public bool HasDependencies => Dependencies.Length > 0;
        public bool HasIntegrations => Integrations.Length > 0;
        public bool HasTags => Tags.Length > 0;
        public bool HasAuthors => Authors.Length > 0;

        public bool UpdateAvailable => State is InstalledState { Updated: false };

        public string UpdateText  => $"\u279E {Version}";

        private string _settingsFile = string.Empty;
        public string SettingsFile
        {
            get
            {
                // dont find it if its already found
                if (string.IsNullOrEmpty(_settingsFile))
                {
                    _settingsFile = GlobalSettingsFinder.GetSettingsFile(this) ?? string.Empty;
                }

                return _settingsFile;
            }
        }
        
        public bool HasSettings => State is InstalledState && !string.IsNullOrEmpty(SettingsFile);

        public string VersionText => State switch
        {
            InstalledState st => st.Version.ToString(),
            NotInstalledState => Version.ToString(),
            NotInModLinksState => Version.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(_state))
        };

        public async Task OnUpdate(IInstaller inst, Action<ModProgressArgs> setProgress)
        {
            ModState orig = State;

            try
            {
                if (State is not InstalledState { Updated: false, Enabled: var enabled })
                    throw new InvalidOperationException("Not able to be updated!");

                setProgress(new ModProgressArgs());

                await inst.Install(this, setProgress, enabled);
                
                setProgress(new ModProgressArgs { Completed = true });
            }
            catch
            {
                State = orig;
                throw;
            }
        }

        public async Task OnInstall(IInstaller inst, Action<ModProgressArgs> setProgress)
        {
            ModState origState = State;
            
            try
            {
                if (State is InstalledState or NotInModLinksState)
                {
                    await inst.Uninstall(this);
                }
                else
                {
                    State = (NotInstalledState) State with { Installing = true };

                    setProgress(new ModProgressArgs());

                    await inst.Install(this, setProgress, true);

                    setProgress(new ModProgressArgs { Completed = true });
                }
            }
            catch
            {
                State = origState;
                throw;
            }
        }
        
        public void CallOnPropertyChanged(string propertyName)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
        }

        public void OpenSettingsFile()
        {
            try
            {
                if (HasSettings && File.Exists(SettingsFile))
                {
                    var process = new Process();
                    process.StartInfo = new ProcessStartInfo()
                    {
                        UseShellExecute = true,
                        FileName = SettingsFile
                    };

                    process.Start();
                }
                else
                {
                    throw new Exception("Settings not there");
                }
            }
            catch (Exception)
            {
                _settingsFile = string.Empty;
                CallOnPropertyChanged(nameof(HasSettings));
            }
        }

        public static ModItem Empty(
            ModState? state = null,
            Version? version = null,
            string[]? dependencies = null ,
            string? link = null ,
            string? shasum = null ,
            string? name = null ,
            string? description = null ,
            string? repository = null ,
            string[]? tags = null ,
            string[]? integrations = null ,
            string[]? authors = null
        )
        {
            return new ModItem(
                state ?? new NotInModLinksState(),
                version ?? new Version(0, 0, 0),
                dependencies ?? Array.Empty<string>(),
                link ?? string.Empty,
                shasum ?? string.Empty,
                name ?? string.Empty,
                description ?? string.Empty,
                repository ?? string.Empty,
                tags ?? Array.Empty<string>(),
                integrations ?? Array.Empty<string>(),
                authors ?? Array.Empty<string>()
            );
        }

        #region Equality
        public bool Equals(ModItem? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return _state.Equals(other._state)
                && Version.Equals(other.Version)
                && Dependencies.Zip(other.Dependencies).All(tuple => tuple.First == tuple.Second)
                && Link == other.Link
                && Name == other.Name
                && Description == other.Description;
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            
            return obj.GetType() == GetType() && Equals((ModItem) obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Version, Dependencies, Link, Name, Description);
        }

        public static bool operator ==(ModItem? left, ModItem? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ModItem? left, ModItem? right)
        {
            return !Equals(left, right);
        }
        #endregion
    }
}