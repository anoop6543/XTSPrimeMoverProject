using System;

namespace XTSPrimeMoverProject.ViewModels
{
    public sealed class EmbeddedWebHmiScreenViewModel
    {
        public EmbeddedWebHmiScreenViewModel(string displayName, string description, Uri sourceUri)
        {
            DisplayName = displayName;
            Description = description;
            SourceUri = sourceUri;
        }

        public string DisplayName { get; }
        public string Description { get; }
        public Uri SourceUri { get; }
        public string UrlDisplay => SourceUri.AbsoluteUri;
    }
}
