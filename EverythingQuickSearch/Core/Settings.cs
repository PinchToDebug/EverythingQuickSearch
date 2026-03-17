using System.ComponentModel;

namespace EverythingQuickSearch.Core
{
    public class Settings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private RegistryHelper _regHelper;
        private bool _transparentBackground;

        public bool TransparentBackground
        {
            get => _transparentBackground;
            set
            {
                if (_transparentBackground != value)
                {
                    _transparentBackground = value;
                    OnPropertyChanged(nameof(TransparentBackground), value.ToString());
                }
            }
        }

        protected void OnPropertyChanged(string propertyName, string value)
        {
            _regHelper.WriteToRegistryRoot(propertyName, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public Settings()
        {
            _regHelper = new RegistryHelper("EverythingQuickSearch");
           
            var v = _regHelper.ReadKeyValueRoot("TransparentBackground");
            if (v != null) TransparentBackground = (bool)v;
        }

    }
}
