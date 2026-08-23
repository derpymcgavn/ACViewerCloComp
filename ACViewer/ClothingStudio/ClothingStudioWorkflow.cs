using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ACE.DatLoader.FileTypes;
using ACViewer.CustomPalettes;
using ACViewer.CustomTextures;

namespace ACViewer.ClothingStudio
{
    public enum ClothingStudioStep
    {
        SelectClothing,
        AssignId,
        PickSetup,
        EditPieces,
        EditPalettes,
        Preview,
        Validate,
        Export
    }

    public enum ClothingStudioIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class ClothingStudioValidationIssue
    {
        public ClothingStudioIssueSeverity Severity { get; init; }
        public string Message { get; init; }
        public string DisplayName => $"{Severity}: {Message}";
    }

    public sealed class ClothingStudioStepState : INotifyPropertyChanged
    {
        private bool _isActive;
        private bool _isComplete;
        private string _hint;

        public ClothingStudioStep Step { get; init; }
        public string Title { get; init; }
        public bool IsActive { get => _isActive; set => SetField(ref _isActive, value); }
        public bool IsComplete { get => _isComplete; set => SetField(ref _isComplete, value); }
        public string Hint { get => _hint; set => SetField(ref _hint, value); }

        public event PropertyChangedEventHandler PropertyChanged;
        private void SetField<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class ClothingStudioProjectState : INotifyPropertyChanged
    {
        private uint _clothingId;
        private uint _setupId;
        private uint _paletteTemplateId;
        private bool _hasCustomPalette;
        private bool _hasTextureOverrides;
        private bool _hasModelOverrides;
        private bool _isDirty;
        private string _summary = "Choose or clone a clothing item to begin.";

        public uint ClothingId { get => _clothingId; set => SetField(ref _clothingId, value); }
        public uint SetupId { get => _setupId; set => SetField(ref _setupId, value); }
        public uint PaletteTemplateId { get => _paletteTemplateId; set => SetField(ref _paletteTemplateId, value); }
        public bool HasCustomPalette { get => _hasCustomPalette; set => SetField(ref _hasCustomPalette, value); }
        public bool HasTextureOverrides { get => _hasTextureOverrides; set => SetField(ref _hasTextureOverrides, value); }
        public bool HasModelOverrides { get => _hasModelOverrides; set => SetField(ref _hasModelOverrides, value); }
        public bool IsDirty { get => _isDirty; set => SetField(ref _isDirty, value); }
        public string Summary { get => _summary; set => SetField(ref _summary, value); }

        public event PropertyChangedEventHandler PropertyChanged;
        private void SetField<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// A UI-independent workflow coordinator for DerpACE Clothing Studio.
    /// Old ACViewer controls can report user intent here while the new guided UI is built around it.
    /// </summary>
    public sealed class ClothingStudioWorkflowService : INotifyPropertyChanged
    {
        public static ClothingStudioWorkflowService Instance { get; } = new();

        private ClothingStudioStep _activeStep = ClothingStudioStep.SelectClothing;

        public ClothingStudioProjectState Project { get; } = new();
        public ObservableCollection<ClothingStudioStepState> Steps { get; } = new();
        public ObservableCollection<ClothingStudioValidationIssue> Issues { get; } = new();

        public ClothingStudioStep ActiveStep
        {
            get => _activeStep;
            private set
            {
                if (_activeStep == value) return;
                _activeStep = value;
                OnPropertyChanged();
                RefreshSteps();
            }
        }

        private ClothingStudioWorkflowService()
        {
            Steps.Add(new ClothingStudioStepState { Step = ClothingStudioStep.SelectClothing, Title = "1. Clothing", Hint = "Pick, clone, or import a clothing item." });
            Steps.Add(new ClothingStudioStepState { Step = ClothingStudioStep.AssignId, Title = "2. ID", Hint = "Assign a safe ClothingMod ID." });
            Steps.Add(new ClothingStudioStepState { Step = ClothingStudioStep.PickSetup, Title = "3. Setup", Hint = "Choose the body/setup preview target." });
            Steps.Add(new ClothingStudioStepState { Step = ClothingStudioStep.EditPieces, Title = "4. Pieces", Hint = "Replace armor piece models and textures." });
            Steps.Add(new ClothingStudioStepState { Step = ClothingStudioStep.EditPalettes, Title = "5. Palettes", Hint = "Add palette IDs and ranges for armor color bands." });
            Steps.Add(new ClothingStudioStepState { Step = ClothingStudioStep.Preview, Title = "6. Preview", Hint = "Check the full outfit in 3D." });
            Steps.Add(new ClothingStudioStepState { Step = ClothingStudioStep.Validate, Title = "7. Validate", Hint = "Catch missing IDs, bad ranges, or unsafe collisions." });
            Steps.Add(new ClothingStudioStepState { Step = ClothingStudioStep.Export, Title = "8. Export", Hint = "Export ClothingMod JSON or DAT package." });
            RefreshSteps();
        }

        public void SelectClothing(ClothingTable clothing)
        {
            Project.ClothingId = clothing?.Id ?? 0;
            Project.IsDirty = false;
            ActiveStep = clothing == null ? ClothingStudioStep.SelectClothing : ClothingStudioStep.AssignId;
            RefreshSummary();
            Validate(clothing);
        }

        public void SelectSetup(uint setupId)
        {
            Project.SetupId = setupId;
            ActiveStep = setupId == 0 ? ClothingStudioStep.PickSetup : ClothingStudioStep.EditPieces;
            RefreshSummary();
        }

        public void SelectPaletteTemplate(uint paletteTemplateId, CustomPaletteDefinition customDefinition = null)
        {
            Project.PaletteTemplateId = paletteTemplateId;
            Project.HasCustomPalette = customDefinition?.Entries?.Count > 0;
            if (Project.HasCustomPalette || paletteTemplateId != 0)
                ActiveStep = ClothingStudioStep.EditPalettes;
            RefreshSummary();
        }

        public void MarkTextureOverrides(bool hasOverrides)
        {
            Project.HasTextureOverrides = hasOverrides;
            Project.IsDirty |= hasOverrides;
            if (hasOverrides) ActiveStep = ClothingStudioStep.EditPieces;
            RefreshSummary();
        }

        public void MarkModelOverrides(bool hasOverrides)
        {
            Project.HasModelOverrides = hasOverrides;
            Project.IsDirty |= hasOverrides;
            if (hasOverrides) ActiveStep = ClothingStudioStep.EditPieces;
            RefreshSummary();
        }

        public IReadOnlyList<ClothingStudioValidationIssue> Validate(ClothingTable clothing)
        {
            Issues.Clear();
            if (clothing == null)
            {
                Issues.Add(new ClothingStudioValidationIssue { Severity = ClothingStudioIssueSeverity.Info, Message = "No clothing item selected yet." });
                RefreshSteps();
                return Issues.ToList();
            }

            foreach (var error in ClothingModService.Validate(clothing))
                Issues.Add(new ClothingStudioValidationIssue { Severity = ClothingStudioIssueSeverity.Error, Message = error });

            if (!Project.HasCustomPalette && !Project.HasTextureOverrides && !Project.HasModelOverrides)
                Issues.Add(new ClothingStudioValidationIssue { Severity = ClothingStudioIssueSeverity.Info, Message = "No custom palette, texture, or model overrides are active yet." });

            if (Issues.All(issue => issue.Severity != ClothingStudioIssueSeverity.Error))
                ActiveStep = ClothingStudioStep.Export;
            RefreshSteps();
            RefreshSummary();
            return Issues.ToList();
        }

        private void RefreshSteps()
        {
            foreach (var step in Steps)
            {
                step.IsActive = step.Step == ActiveStep;
                step.IsComplete = IsStepComplete(step.Step);
            }
        }

        private bool IsStepComplete(ClothingStudioStep step) => step switch
        {
            ClothingStudioStep.SelectClothing => Project.ClothingId != 0,
            ClothingStudioStep.AssignId => Project.ClothingId != 0 && (Project.ClothingId >> 24) == 0x10,
            ClothingStudioStep.PickSetup => Project.SetupId != 0,
            ClothingStudioStep.EditPieces => Project.HasTextureOverrides || Project.HasModelOverrides || Project.HasCustomPalette,
            ClothingStudioStep.EditPalettes => Project.HasCustomPalette,
            ClothingStudioStep.Preview => Project.ClothingId != 0 && Project.SetupId != 0,
            ClothingStudioStep.Validate => Issues.All(issue => issue.Severity != ClothingStudioIssueSeverity.Error),
            ClothingStudioStep.Export => Project.ClothingId != 0 && Issues.All(issue => issue.Severity != ClothingStudioIssueSeverity.Error),
            _ => false
        };

        private void RefreshSummary()
        {
            var parts = new List<string>();
            parts.Add(Project.ClothingId == 0 ? "No clothing" : $"Clothing 0x{Project.ClothingId:X8}");
            if (Project.SetupId != 0) parts.Add($"setup 0x{Project.SetupId:X8}");
            if (Project.PaletteTemplateId != 0) parts.Add($"palette template 0x{Project.PaletteTemplateId:X8}");
            if (Project.HasCustomPalette) parts.Add("custom palettes");
            if (Project.HasTextureOverrides) parts.Add("texture overrides");
            if (Project.HasModelOverrides) parts.Add("model overrides");
            Project.Summary = string.Join(" • ", parts);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
