/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using NLog;

namespace Chummer
{
    /// <summary>
    /// A Martial Art.
    /// </summary>
    [DebuggerDisplay("{DisplayName(GlobalSettings.DefaultLanguage)}")]
    public sealed class MartialArt : IHasChildren<MartialArtTechnique>, IHasName, IHasInternalId, IHasXmlDataNode, IHasNotes, ICanRemove, IHasSource, IDisposable, IAsyncDisposable
    {
        private static readonly Lazy<Logger> s_ObjLogger = new Lazy<Logger>(LogManager.GetCurrentClassLogger);
        private static Logger Log => s_ObjLogger.Value;
        private Guid _guiID;
        private Guid _guiSourceID;
        private string _strName = string.Empty;
        private string _strSource = string.Empty;
        private string _strPage = string.Empty;
        private int _intKarmaCost = 7;
        private readonly TaggedObservableCollection<MartialArtTechnique> _lstTechniques = new TaggedObservableCollection<MartialArtTechnique>();
        private string _strNotes = string.Empty;
        private Color _colNotes = ColorManager.HasNotesColor;
        private readonly Character _objCharacter;
        private bool _blnIsQuality;

        #region Create, Save, Load, and Print Methods

        public MartialArt(Character objCharacter)
        {
            _objCharacter = objCharacter;
            _guiID = Guid.NewGuid();

            _lstTechniques.AddTaggedCollectionChanged(this, TechniquesOnCollectionChanged);
        }

        private void TechniquesOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            List<MartialArtTechnique> lstImprovementSourcesToProcess = new List<MartialArtTechnique>(e.NewItems?.Count ?? 0);
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    // ReSharper disable once PossibleNullReferenceException
                    foreach (MartialArtTechnique objNewItem in e.NewItems)
                    {
                        objNewItem.Parent = this;
                        lstImprovementSourcesToProcess.Add(objNewItem);
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (MartialArtTechnique objOldItem in e.OldItems)
                    {
                        if (objOldItem.Parent != this)
                            continue;
                        objOldItem.Parent = null;
                    }
                    break;

                case NotifyCollectionChangedAction.Replace:
                    // ReSharper disable once AssignNullToNotNullAttribute
                    HashSet<MartialArtTechnique> setNewItems = e.NewItems.OfType<MartialArtTechnique>().ToHashSet();
                    foreach (MartialArtTechnique objOldItem in e.OldItems)
                    {
                        if (setNewItems.Contains(objOldItem))
                            continue;
                        if (objOldItem.Parent != this)
                            continue;
                        objOldItem.Parent = null;
                    }

                    foreach (MartialArtTechnique objNewItem in setNewItems)
                    {
                        objNewItem.Parent = this;
                        lstImprovementSourcesToProcess.Add(objNewItem);
                    }
                    break;

                case NotifyCollectionChangedAction.Reset:
                case NotifyCollectionChangedAction.Move:
                    break;
            }
            if (lstImprovementSourcesToProcess.Count > 0 && _objCharacter?.IsLoading == false)
            {
                using (new FetchSafelyFromPool<Dictionary<INotifyMultiplePropertyChanged, HashSet<string>>>(
                           Utils.DictionaryForMultiplePropertyChangedPool,
                           out Dictionary<INotifyMultiplePropertyChanged, HashSet<string>> dicChangedProperties))
                {
                    try
                    {
                        foreach (MartialArtTechnique objNewItem in lstImprovementSourcesToProcess)
                        {
                            // Needed in order to properly process named sources where
                            // the tooltip was built before the object was added to the character
                            foreach (Improvement objImprovement in _objCharacter.Improvements)
                            {
                                if (objImprovement.SourceName != objNewItem.InternalId || !objImprovement.Enabled)
                                    continue;
                                foreach ((INotifyMultiplePropertyChanged objToUpdate, string strPropertyName) in
                                         objImprovement.GetRelevantPropertyChangers())
                                {
                                    if (!dicChangedProperties.TryGetValue(objToUpdate,
                                                                          out HashSet<string> setChangedProperties))
                                    {
                                        setChangedProperties = Utils.StringHashSetPool.Get();
                                        dicChangedProperties.Add(objToUpdate, setChangedProperties);
                                    }

                                    setChangedProperties.Add(strPropertyName);
                                }
                            }
                        }

                        foreach (KeyValuePair<INotifyMultiplePropertyChanged, HashSet<string>> kvpToUpdate in
                                 dicChangedProperties)
                        {
                            kvpToUpdate.Key.OnMultiplePropertyChanged(kvpToUpdate.Value.ToList());
                        }
                    }
                    finally
                    {
                        foreach (HashSet<string> setToReturn in dicChangedProperties.Values)
                            Utils.StringHashSetPool.Return(setToReturn);
                    }
                }
            }
        }

        /// <summary>
        /// Create a Martial Art from an XmlNode.
        /// </summary>
        /// <param name="objXmlArtNode">XmlNode to create the object from.</param>
        public void Create(XmlNode objXmlArtNode)
        {
            if (!objXmlArtNode.TryGetField("id", Guid.TryParse, out _guiSourceID))
            {
                Log.Warn(new object[] { "Missing id field for xmlnode", objXmlArtNode });
                Utils.BreakIfDebug();
            }

            if (objXmlArtNode.TryGetStringFieldQuickly("name", ref _strName))
            {
                _objCachedMyXmlNode = null;
                _objCachedMyXPathNode = null;
            }

            objXmlArtNode.TryGetStringFieldQuickly("source", ref _strSource);
            objXmlArtNode.TryGetStringFieldQuickly("page", ref _strPage);
            objXmlArtNode.TryGetInt32FieldQuickly("cost", ref _intKarmaCost);
            if (!objXmlArtNode.TryGetMultiLineStringFieldQuickly("altnotes", ref _strNotes))
                objXmlArtNode.TryGetMultiLineStringFieldQuickly("notes", ref _strNotes);

            string sNotesColor = ColorTranslator.ToHtml(ColorManager.HasNotesColor);
            objXmlArtNode.TryGetStringFieldQuickly("notesColor", ref sNotesColor);
            _colNotes = ColorTranslator.FromHtml(sNotesColor);

            _blnIsQuality = objXmlArtNode["isquality"]?.InnerText == bool.TrueString;

            if (objXmlArtNode["bonus"] != null)
            {
                ImprovementManager.CreateImprovements(_objCharacter, Improvement.ImprovementSource.MartialArt, InternalId,
                    objXmlArtNode["bonus"], 1, CurrentDisplayNameShort);
            }
            if (string.IsNullOrEmpty(Notes))
            {
                Notes = CommonFunctions.GetBookNotes(objXmlArtNode, Name, CurrentDisplayName, Source, Page,
                    DisplayPage(GlobalSettings.Language), _objCharacter);
            }
        }

        private SourceString _objCachedSourceDetail;

        public SourceString SourceDetail
        {
            get
            {
                if (_objCachedSourceDetail == default)
                    _objCachedSourceDetail = SourceString.GetSourceString(Source, DisplayPage(GlobalSettings.Language),
                        GlobalSettings.Language, GlobalSettings.CultureInfo, _objCharacter);
                return _objCachedSourceDetail;
            }
        }

        /// <summary>
        /// Save the object's XML to the XmlWriter.
        /// </summary>
        /// <param name="objWriter">XmlTextWriter to write with.</param>
        public void Save(XmlWriter objWriter)
        {
            if (objWriter == null)
                return;
            objWriter.WriteStartElement("martialart");
            objWriter.WriteElementString("name", _strName);
            objWriter.WriteElementString("sourceid", SourceIDString);
            objWriter.WriteElementString("guid", InternalId);
            objWriter.WriteElementString("source", _strSource);
            objWriter.WriteElementString("page", _strPage);
            objWriter.WriteElementString("cost", _intKarmaCost.ToString(GlobalSettings.InvariantCultureInfo));
            objWriter.WriteElementString("isquality", _blnIsQuality.ToString(GlobalSettings.InvariantCultureInfo));
            objWriter.WriteStartElement("martialarttechniques");
            foreach (MartialArtTechnique objTechnique in _lstTechniques)
            {
                objTechnique.Save(objWriter);
            }
            objWriter.WriteEndElement();
            objWriter.WriteElementString("notes", _strNotes.CleanOfInvalidUnicodeChars());
            objWriter.WriteElementString("notesColor", ColorTranslator.ToHtml(_colNotes));
            objWriter.WriteEndElement();
        }

        /// <summary>
        /// Load the Martial Art from the XmlNode.
        /// </summary>
        /// <param name="objNode">XmlNode to load.</param>
        public void Load(XmlNode objNode)
        {
            if (objNode == null)
                return;
            if (!objNode.TryGetField("guid", Guid.TryParse, out _guiID))
            {
                _guiID = Guid.NewGuid();
            }

            objNode.TryGetStringFieldQuickly("name", ref _strName);
            _objCachedMyXmlNode = null;
            _objCachedMyXPathNode = null;

            if (!objNode.TryGetGuidFieldQuickly("sourceid", ref _guiSourceID))
            {
                this.GetNodeXPath()?.TryGetGuidFieldQuickly("id", ref _guiSourceID);
            }
            objNode.TryGetStringFieldQuickly("source", ref _strSource);
            objNode.TryGetStringFieldQuickly("page", ref _strPage);
            objNode.TryGetInt32FieldQuickly("cost", ref _intKarmaCost);
            objNode.TryGetBoolFieldQuickly("isquality", ref _blnIsQuality);

            using (XmlNodeList xmlLegacyTechniqueList = objNode.SelectNodes("martialartadvantages/martialartadvantage"))
            {
                if (xmlLegacyTechniqueList != null)
                {
                    foreach (XmlNode nodTechnique in xmlLegacyTechniqueList)
                    {
                        MartialArtTechnique objTechnique = new MartialArtTechnique(_objCharacter);
                        objTechnique.Load(nodTechnique);
                        _lstTechniques.Add(objTechnique);
                    }
                }
            }

            using (XmlNodeList xmlTechniqueList = objNode.SelectNodes("martialarttechniques/martialarttechnique"))
            {
                if (xmlTechniqueList != null)
                {
                    foreach (XmlNode nodTechnique in xmlTechniqueList)
                    {
                        MartialArtTechnique objTechnique = new MartialArtTechnique(_objCharacter);
                        objTechnique.Load(nodTechnique);
                        _lstTechniques.Add(objTechnique);
                    }
                }
            }

            objNode.TryGetMultiLineStringFieldQuickly("notes", ref _strNotes);

            string sNotesColor = ColorTranslator.ToHtml(ColorManager.HasNotesColor);
            objNode.TryGetStringFieldQuickly("notesColor", ref sNotesColor);
            _colNotes = ColorTranslator.FromHtml(sNotesColor);
        }

        /// <summary>
        /// Print the object's XML to the XmlWriter.
        /// </summary>
        /// <param name="objWriter">XmlTextWriter to write with.</param>
        /// <param name="objCulture">Culture in which to print.</param>
        /// <param name="strLanguageToPrint">Language in which to print</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public async ValueTask Print(XmlWriter objWriter, CultureInfo objCulture, string strLanguageToPrint, CancellationToken token = default)
        {
            if (objWriter == null)
                return;
            // <martialart>
            XmlElementWriteHelper objBaseElement = await objWriter.StartElementAsync("martialart", token: token).ConfigureAwait(false);
            try
            {
                await objWriter.WriteElementStringAsync("guid", InternalId, token: token).ConfigureAwait(false);
                await objWriter.WriteElementStringAsync("sourceid", SourceIDString, token: token).ConfigureAwait(false);
                await objWriter
                      .WriteElementStringAsync(
                          "name", await DisplayNameShortAsync(strLanguageToPrint, token).ConfigureAwait(false),
                          token: token).ConfigureAwait(false);
                await objWriter
                      .WriteElementStringAsync(
                          "fullname", await DisplayNameAsync(strLanguageToPrint, token).ConfigureAwait(false),
                          token: token).ConfigureAwait(false);
                await objWriter.WriteElementStringAsync("name_english", Name, token: token).ConfigureAwait(false);
                await objWriter
                      .WriteElementStringAsync(
                          "source",
                          await _objCharacter.LanguageBookShortAsync(Source, strLanguageToPrint, token)
                                             .ConfigureAwait(false), token: token).ConfigureAwait(false);
                await objWriter
                      .WriteElementStringAsync(
                          "page", await DisplayPageAsync(strLanguageToPrint, token).ConfigureAwait(false), token: token)
                      .ConfigureAwait(false);
                await objWriter.WriteElementStringAsync("cost", Cost.ToString(objCulture), token: token)
                               .ConfigureAwait(false);
                // <martialarttechniques>
                XmlElementWriteHelper objTechniquesElement
                    = await objWriter.StartElementAsync("martialarttechniques", token: token).ConfigureAwait(false);
                try
                {
                    foreach (MartialArtTechnique objTechnique in Techniques)
                    {
                        await objTechnique.Print(objWriter, strLanguageToPrint, token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    // </martialarttechniques>
                    await objTechniquesElement.DisposeAsync().ConfigureAwait(false);
                }

                if (GlobalSettings.PrintNotes)
                    await objWriter.WriteElementStringAsync("notes", Notes, token: token).ConfigureAwait(false);
            }
            finally
            {
                // </martialart>
                await objBaseElement.DisposeAsync().ConfigureAwait(false);
            }
        }

        #endregion Create, Save, Load, and Print Methods

        #region Properties

        /// <summary>
        /// Name.
        /// </summary>
        public string Name
        {
            get => _strName;
            set
            {
                if (_strName == value)
                    return;
                if (SourceID == Guid.Empty)
                {
                    _objCachedMyXmlNode = null;
                    _objCachedMyXPathNode = null;
                }
                _strName = value;
            }
        }

        /// <summary>
        /// Identifier of the object within data files.
        /// </summary>
        public Guid SourceID
        {
            get => _guiSourceID;
            set
            {
                if (_guiSourceID == value)
                    return;
                _objCachedMyXmlNode = null;
                _objCachedMyXPathNode = null;
                _guiSourceID = value;
            }
        }

        /// <summary>
        /// String-formatted identifier of the <inheritdoc cref="SourceID"/> from the data files.
        /// </summary>
        public string SourceIDString => _guiSourceID.ToString("D", GlobalSettings.InvariantCultureInfo);

        public string InternalId => _guiID.ToString("D", GlobalSettings.InvariantCultureInfo);

        /// <summary>
        /// The name of the object as it should be displayed on printouts (translated name only).
        /// </summary>
        public string DisplayNameShort(string strLanguage)
        {
            // Get the translated name if applicable.
            if (strLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                return Name;

            return this.GetNodeXPath(strLanguage)?.SelectSingleNodeAndCacheExpression("translate")?.Value ?? Name;
        }

        /// <summary>
        /// The name of the object as it should be displayed on printouts (translated name only).
        /// </summary>
        public async ValueTask<string> DisplayNameShortAsync(string strLanguage, CancellationToken token = default)
        {
            // Get the translated name if applicable.
            if (strLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                return Name;

            XPathNavigator objNode = await this.GetNodeXPathAsync(strLanguage, token: token).ConfigureAwait(false);
            return objNode != null ? (await objNode.SelectSingleNodeAndCacheExpressionAsync("translate", token: token).ConfigureAwait(false))?.Value ?? Name : Name;
        }

        public string CurrentDisplayNameShort => DisplayNameShort(GlobalSettings.Language);

        public ValueTask<string> GetCurrentDisplayNameShortAsync(CancellationToken token = default) =>
            DisplayNameShortAsync(GlobalSettings.Language, token);

        /// <summary>
        /// The name of the object as it should be displayed in lists. Name (Extra).
        /// </summary>
        public string DisplayName(string strLanguage)
        {
            return DisplayNameShort(strLanguage);
        }

        /// <summary>
        /// The name of the object as it should be displayed in lists. Name (Extra).
        /// </summary>
        public ValueTask<string> DisplayNameAsync(string strLanguage, CancellationToken token = default)
        {
            return DisplayNameShortAsync(strLanguage, token);
        }

        public string CurrentDisplayName => DisplayName(GlobalSettings.Language);

        public ValueTask<string> GetCurrentDisplayNameAsync(CancellationToken token = default) =>
            DisplayNameAsync(GlobalSettings.Language, token);

        /// <summary>
        /// Sourcebook.
        /// </summary>
        public string Source
        {
            get => _strSource;
            set => _strSource = value;
        }

        /// <summary>
        /// Sourcebook Page Number.
        /// </summary>
        public string Page
        {
            get => _strPage;
            set => _strPage = value;
        }

        /// <summary>
        /// Sourcebook Page Number using a given language file.
        /// Returns Page if not found or the string is empty.
        /// </summary>
        /// <param name="strLanguage">Language file keyword to use.</param>
        /// <returns></returns>
        public string DisplayPage(string strLanguage)
        {
            if (strLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                return Page;
            string s = this.GetNodeXPath(strLanguage)?.SelectSingleNodeAndCacheExpression("altpage")?.Value ?? Page;
            return !string.IsNullOrWhiteSpace(s) ? s : Page;
        }

        /// <summary>
        /// Sourcebook Page Number using a given language file.
        /// Returns Page if not found or the string is empty.
        /// </summary>
        /// <param name="strLanguage">Language file keyword to use.</param>
        /// <param name="token">Cancellation token to listen to.</param>
        /// <returns></returns>
        public async ValueTask<string> DisplayPageAsync(string strLanguage, CancellationToken token = default)
        {
            if (strLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                return Page;
            XPathNavigator objNode = await this.GetNodeXPathAsync(strLanguage, token: token).ConfigureAwait(false);
            string s = objNode != null
                ? (await objNode.SelectSingleNodeAndCacheExpressionAsync("altpage", token: token).ConfigureAwait(false))?.Value ?? Page
                : Page;
            return !string.IsNullOrWhiteSpace(s) ? s : Page;
        }

        /// <summary>
        /// Karma Cost (usually 7).
        /// </summary>
        public int Cost
        {
            get => _intKarmaCost;
            set => _intKarmaCost = value;
        }

        /// <summary>
        /// Is from a quality.
        /// </summary>
        public bool IsQuality
        {
            get => _blnIsQuality;
            set => _blnIsQuality = value;
        }

        /// <summary>
        /// Selected Martial Arts Techniques.
        /// </summary>
        public TaggedObservableCollection<MartialArtTechnique> Techniques => _lstTechniques;

        public TaggedObservableCollection<MartialArtTechnique> Children => Techniques;

        /// <summary>
        /// Notes.
        /// </summary>
        public string Notes
        {
            get => _strNotes;
            set => _strNotes = value;
        }

        /// <summary>
        /// Forecolor to use for Notes in treeviews.
        /// </summary>
        public Color NotesColor
        {
            get => _colNotes;
            set => _colNotes = value;
        }

        private XmlNode _objCachedMyXmlNode;
        private string _strCachedXmlNodeLanguage = string.Empty;

        public async Task<XmlNode> GetNodeCoreAsync(bool blnSync, string strLanguage, CancellationToken token = default)
        {
            if (_objCachedMyXmlNode != null && strLanguage == _strCachedXmlNodeLanguage
                                            && !GlobalSettings.LiveCustomData)
                return _objCachedMyXmlNode;
            _objCachedMyXmlNode = (blnSync
                    // ReSharper disable once MethodHasAsyncOverload
                    ? _objCharacter.LoadData("martialarts.xml", strLanguage, token: token)
                    : await _objCharacter.LoadDataAsync("martialarts.xml", strLanguage, token: token).ConfigureAwait(false))
                .SelectSingleNode(SourceID == Guid.Empty
                                      ? "/chummer/martialarts/martialart[name = "
                                        + Name.CleanXPath() + ']'
                                      : "/chummer/martialarts/martialart[id = "
                                        + SourceIDString.CleanXPath()
                                        + " or id = " + SourceIDString
                                                        .ToUpperInvariant().CleanXPath()
                                        + ']');
            _strCachedXmlNodeLanguage = strLanguage;
            return _objCachedMyXmlNode;
        }

        private XPathNavigator _objCachedMyXPathNode;
        private string _strCachedXPathNodeLanguage = string.Empty;

        public async Task<XPathNavigator> GetNodeXPathCoreAsync(bool blnSync, string strLanguage, CancellationToken token = default)
        {
            if (_objCachedMyXPathNode != null && strLanguage == _strCachedXPathNodeLanguage
                                              && !GlobalSettings.LiveCustomData)
                return _objCachedMyXPathNode;
            _objCachedMyXPathNode = (blnSync
                    // ReSharper disable once MethodHasAsyncOverload
                    ? _objCharacter.LoadDataXPath("martialarts.xml", strLanguage, token: token)
                    : await _objCharacter.LoadDataXPathAsync("martialarts.xml", strLanguage, token: token).ConfigureAwait(false))
                .SelectSingleNode(SourceID == Guid.Empty
                                      ? "/chummer/martialarts/martialart[name = "
                                        + Name.CleanXPath() + ']'
                                      : "/chummer/martialarts/martialart[id = "
                                        + SourceIDString.CleanXPath()
                                        + " or id = " + SourceIDString
                                                        .ToUpperInvariant().CleanXPath()
                                        + ']');
            _strCachedXPathNodeLanguage = strLanguage;
            return _objCachedMyXPathNode;
        }

        #endregion Properties

        #region Methods

        public TreeNode CreateTreeNode(ContextMenuStrip cmsMartialArt, ContextMenuStrip cmsMartialArtTechnique)
        {
            if (IsQuality && !string.IsNullOrEmpty(Source) && !_objCharacter.Settings.BookEnabled(Source))
                return null;

            TreeNode objNode = new TreeNode
            {
                Name = InternalId,
                Text = CurrentDisplayName,
                Tag = this,
                ContextMenuStrip = cmsMartialArt,
                ForeColor = PreferredColor,
                ToolTipText = Notes.WordWrap()
            };

            TreeNodeCollection lstChildNodes = objNode.Nodes;
            foreach (MartialArtTechnique objTechnique in Techniques)
            {
                TreeNode objLoopNode = objTechnique.CreateTreeNode(cmsMartialArtTechnique);
                if (objLoopNode != null)
                {
                    lstChildNodes.Add(objLoopNode);
                    objNode.Expand();
                }
            }

            return objNode;
        }

        public static async ValueTask<bool> Purchase(Character objCharacter, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            bool blnReturn = false;
            bool blnAddAgain;
            do
            {
                using (ThreadSafeForm<SelectMartialArt> frmPickMartialArt
                       = await ThreadSafeForm<SelectMartialArt>.GetAsync(
                           () => new SelectMartialArt(objCharacter), token).ConfigureAwait(false))
                {
                    if (await frmPickMartialArt.ShowDialogSafeAsync(objCharacter, token).ConfigureAwait(false) == DialogResult.Cancel)
                        return blnReturn;

                    blnAddAgain = frmPickMartialArt.MyForm.AddAgain;
                    // Open the Martial Arts XML file and locate the selected piece.
                    XmlNode objXmlArt
                        = (await objCharacter.LoadDataAsync("martialarts.xml", token: token).ConfigureAwait(false)).SelectSingleNode(
                            "/chummer/martialarts/martialart[id = "
                            + frmPickMartialArt.MyForm.SelectedMartialArt.CleanXPath() + ']');

                    MartialArt objMartialArt = new MartialArt(objCharacter);
                    objMartialArt.Create(objXmlArt);

                    if (objCharacter.Created)
                    {
                        int intKarmaCost = objMartialArt.Cost;
                        if (intKarmaCost > await objCharacter.GetKarmaAsync(token).ConfigureAwait(false))
                        {
                            Program.ShowMessageBox(
                                await LanguageManager.GetStringAsync("Message_NotEnoughKarma", token: token).ConfigureAwait(false),
                                await LanguageManager.GetStringAsync("MessageTitle_NotEnoughKarma", token: token).ConfigureAwait(false),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                            await ImprovementManager.RemoveImprovementsAsync(
                                objCharacter, Improvement.ImprovementSource.MartialArt, objMartialArt.InternalId,
                                token).ConfigureAwait(false);
                            return blnReturn;
                        }

                        // Create the Expense Log Entry.
                        ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
                        objExpense.Create(intKarmaCost * -1,
                                          await LanguageManager.GetStringAsync(
                                              "String_ExpenseLearnMartialArt", token: token).ConfigureAwait(false) + ' '
                                          + await objMartialArt.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                          ExpenseType.Karma,
                                          DateTime.Now);
                        await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
                        await objCharacter.ModifyKarmaAsync(-intKarmaCost, token).ConfigureAwait(false);

                        ExpenseUndo objUndo = new ExpenseUndo();
                        objUndo.CreateKarma(KarmaExpenseType.AddMartialArt, objMartialArt.InternalId);
                        objExpense.Undo = objUndo;
                    }

                    await objCharacter.MartialArts.AddAsync(objMartialArt, token).ConfigureAwait(false);
                    blnReturn = true;
                }
            } while (blnAddAgain);

            return true;
        }

        public bool Remove(bool blnConfirmDelete = true)
        {
            // Delete the selected Martial Art.
            if (IsQuality)
                return false;
            if (blnConfirmDelete && !CommonFunctions.ConfirmDelete(LanguageManager.GetString("Message_DeleteMartialArt")))
                return false;

            DeleteMartialArt();
            return true;
        }

        public async ValueTask<bool> RemoveAsync(bool blnConfirmDelete = true, CancellationToken token = default)
        {
            // Delete the selected Martial Art.
            if (IsQuality)
                return false;
            if (blnConfirmDelete && !await CommonFunctions
                                           .ConfirmDeleteAsync(
                                               await LanguageManager
                                                     .GetStringAsync("Message_DeleteMartialArt", token: token)
                                                     .ConfigureAwait(false), token).ConfigureAwait(false))
                return false;

            await DeleteMartialArtAsync(token).ConfigureAwait(false);
            return true;
        }

        public decimal DeleteMartialArt()
        {
            _objCharacter.MartialArts.Remove(this);

            decimal decReturn = 0;
            // Remove the Improvements for any Techniques for the Martial Art that is being removed.
            foreach (MartialArtTechnique objTechnique in Techniques.ToList()) // Need ToList() because removing techniques alters parent Art's Techniques list
            {
                decReturn += objTechnique.DeleteTechnique(false);
            }
            decReturn += ImprovementManager.RemoveImprovements(_objCharacter, Improvement.ImprovementSource.MartialArt,
                                                           InternalId);

            Dispose();
            return decReturn;
        }

        public async ValueTask<decimal> DeleteMartialArtAsync(CancellationToken token = default)
        {
            await _objCharacter.MartialArts.RemoveAsync(this, token).ConfigureAwait(false);

            decimal decReturn = 0;
            // Remove the Improvements for any Techniques for the Martial Art that is being removed.
            foreach (MartialArtTechnique objTechnique in Techniques.ToList()) // Need ToList() because removing techniques alters parent Art's Techniques list
            {
                decReturn += await objTechnique.DeleteTechniqueAsync(false, token).ConfigureAwait(false);
            }
            decReturn += await ImprovementManager.RemoveImprovementsAsync(_objCharacter, Improvement.ImprovementSource.MartialArt,
                                                                          InternalId, token).ConfigureAwait(false);

            await DisposeAsync().ConfigureAwait(false);
            return decReturn;
        }

        public Color PreferredColor
        {
            get
            {
                if (!string.IsNullOrEmpty(Notes))
                {
                    return IsQuality
                        ? ColorManager.GenerateCurrentModeDimmedColor(NotesColor)
                        : ColorManager.GenerateCurrentModeColor(NotesColor);
                }
                return IsQuality
                    ? ColorManager.GrayText
                    : ColorManager.WindowText;
            }
        }

        #endregion Methods

        public void SetSourceDetail(Control sourceControl)
        {
            if (_objCachedSourceDetail.Language != GlobalSettings.Language)
                _objCachedSourceDetail = default;
            SourceDetail.SetControl(sourceControl);
        }

        public Task SetSourceDetailAsync(Control sourceControl, CancellationToken token = default)
        {
            if (_objCachedSourceDetail.Language != GlobalSettings.Language)
                _objCachedSourceDetail = default;
            return SourceDetail.SetControlAsync(sourceControl, token);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _lstTechniques.Dispose();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _lstTechniques.DisposeAsync();
        }
    }
}
