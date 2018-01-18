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
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
 using Chummer.Backend.Equipment;
using System.Text;
// ReSharper disable LocalizableElement

namespace Chummer
{
    public partial class frmSelectArmor : Form
    {
        private string _strSelectedArmor = string.Empty;

        private bool _blnAddAgain;
        private static string _strSelectCategory = string.Empty;
        private decimal _decMarkup;
        private Armor _objSelectedArmor = null;

        private readonly XmlDocument _objXmlDocument;
        private readonly Character _objCharacter;

        private readonly List<ListItem> _lstCategory = new List<ListItem>();
        private readonly HashSet<string> _setBlackMarketMaps = new HashSet<string>();
        private int _intRating;
        private bool _blnBlackMarketDiscount;

        #region Control Events
        public frmSelectArmor(Character objCharacter)
        {
            InitializeComponent();
            LanguageManager.TranslateWinForm(GlobalOptions.Language, this);
            lblMarkupLabel.Visible = objCharacter.Created;
            nudMarkup.Visible = objCharacter.Created;
            lblMarkupPercentLabel.Visible = objCharacter.Created;
            _objCharacter = objCharacter;
            MoveControls();
            // Load the Armor information.
            _objXmlDocument = XmlManager.Load("armor.xml");
            CommonFunctions.GenerateBlackMarketMappings(_objCharacter, _objXmlDocument, _setBlackMarketMaps);
        }

        private void frmSelectArmor_Load(object sender, EventArgs e)
        {
            foreach (Label objLabel in Controls.OfType<Label>())
            {
                if (objLabel.Text.StartsWith('['))
                    objLabel.Text = string.Empty;
            }
            if (_objCharacter.Created)
            {
                chkHideOverAvailLimit.Visible = false;
                chkHideOverAvailLimit.Checked = false;
            }
            else
            {
                chkHideOverAvailLimit.Text = chkHideOverAvailLimit.Text.Replace("{0}", _objCharacter.MaximumAvailability.ToString());
                chkHideOverAvailLimit.Checked = _objCharacter.Options.HideItemsOverAvailLimit;
            }

            DataGridViewCellStyle dataGridViewNuyenCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.TopRight,
                Format = _objCharacter.Options.NuyenFormat + '¥',
                NullValue = null
            };
            dataGridViewTextBoxColumn7.DefaultCellStyle = dataGridViewNuyenCellStyle;
            Cost.DefaultCellStyle = dataGridViewNuyenCellStyle;

            // Populate the Armor Category list.
            XmlNodeList objXmlCategoryList = _objXmlDocument.SelectNodes("/chummer/categories/category");
            if (objXmlCategoryList != null)
                foreach (XmlNode objXmlCategory in objXmlCategoryList)
                {
                    string strInnerText = objXmlCategory.InnerText;
                    _lstCategory.Add(new ListItem(strInnerText,
                        objXmlCategory.Attributes?["translate"]?.InnerText ?? strInnerText));
                }
            _lstCategory.Sort(CompareListItems.CompareNames);

            if (_lstCategory.Count > 0)
            {
                _lstCategory.Insert(0, new ListItem("Show All", LanguageManager.GetString("String_ShowAll", GlobalOptions.Language)));
            }

            cboCategory.BeginUpdate();
            cboCategory.ValueMember = "Value";
            cboCategory.DisplayMember = "Name";
            cboCategory.DataSource = _lstCategory;
            chkBlackMarketDiscount.Visible = _objCharacter.BlackMarketDiscount;
            // Select the first Category in the list.
            if (string.IsNullOrEmpty(_strSelectCategory))
                cboCategory.SelectedIndex = 0;
            else
                cboCategory.SelectedValue = _strSelectCategory;

            if (cboCategory.SelectedIndex == -1)
                cboCategory.SelectedIndex = 0;
            cboCategory.EndUpdate();
        }

        private void cmdOK_Click(object sender, EventArgs e)
        {
            AcceptForm();
        }

        private void lstArmor_DoubleClick(object sender, EventArgs e)
        {
            AcceptForm();
        }

        private void cmdCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void lstArmor_SelectedIndexChanged(object sender, EventArgs e)
        {
            string strSelectedId = lstArmor.SelectedValue?.ToString();
            if (string.IsNullOrEmpty(strSelectedId))
                return;

            // Get the information for the selected piece of Armor.
            XmlNode objXmlArmor = _objXmlDocument.SelectSingleNode("/chummer/armors/armor[id = \"" + strSelectedId + "\"]");
            if (objXmlArmor == null)
                return;
            // Create the Armor so we can show its Total Avail (some Armor includes Chemical Seal which adds +6 which wouldn't be factored in properly otherwise).
            Armor objArmor = new Armor(_objCharacter);
            List<Weapon> lstWeapons = new List<Weapon>();
            objArmor.Create(objXmlArmor, 0, lstWeapons, true, true, true);

            _objSelectedArmor = objArmor;

            string strRating = objXmlArmor["rating"]?.InnerText;
            if (!string.IsNullOrEmpty(strRating))
            {
                nudRating.Maximum = Convert.ToInt32(strRating);
                if (chkHideOverAvailLimit.Checked)
                {
                    while (nudRating.Maximum > 1 && !Backend.SelectionShared.CheckAvailRestriction(objXmlArmor, _objCharacter, decimal.ToInt32(nudRating.Maximum)))
                    {
                        nudRating.Maximum -= 1;
                    }
                }
                lblRatingLabel.Visible = true;
                nudRating.Visible = true;
                nudRating.Enabled = true;
                nudRating.Minimum = 1;
                nudRating.Value = 1;
            }
            else
            {
                lblRatingLabel.Visible = false;
                nudRating.Visible = false;
                nudRating.Enabled = false;
                nudRating.Minimum = 0;
                nudRating.Value = 0;
            }

            UpdateArmorInfo();
        }

        private void cboCategory_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshList();
        }

        private void cmdOKAdd_Click(object sender, EventArgs e)
        {
            _blnAddAgain = true;
            cmdOK_Click(sender, e);
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            RefreshList();
        }

        private void chkFreeItem_CheckedChanged(object sender, EventArgs e)
        {
            UpdateArmorInfo();
        }

        private void chkBlackMarketDiscount_CheckedChanged(object sender, EventArgs e)
        {
            UpdateArmorInfo();
        }

        private void nudRating_ValueChanged(object sender, EventArgs e)
        {
            UpdateArmorInfo();
        }

        private void nudMarkup_ValueChanged(object sender, EventArgs e)
        {
            UpdateArmorInfo();
        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down)
            {
                if (lstArmor.SelectedIndex + 1 < lstArmor.Items.Count)
                {
                    lstArmor.SelectedIndex++;
                }
                else if (lstArmor.Items.Count > 0)
                {
                    lstArmor.SelectedIndex = 0;
                }
            }
            if (e.KeyCode == Keys.Up)
            {
                if (lstArmor.SelectedIndex - 1 >= 0)
                {
                    lstArmor.SelectedIndex--;
                }
                else if (lstArmor.Items.Count > 0)
                {
                    lstArmor.SelectedIndex = lstArmor.Items.Count - 1;
                }
            }
        }

        private void txtSearch_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up)
                txtSearch.Select(txtSearch.Text.Length, 0);
        }

        private void tmrSearch_Tick(object sender, EventArgs e)
        {
            tmrSearch.Stop();
            tmrSearch.Enabled = false;

            RefreshList();
        }

        private void dgvArmor_DoubleClick(object sender, EventArgs e)
        {
            AcceptForm();
        }

        private void dgvArmor_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (e.Column.Index != 1) return;
            int intResult = 1;
            if (int.TryParse(e.CellValue1.ToString(), out int intTmp1) &&
                int.TryParse(e.CellValue2.ToString(), out int intTmp2) &&
                intTmp1 < intTmp2)
                intResult = -1;

            e.SortResult = intResult;
            e.Handled = true;
        }
        #endregion

        #region Properties
        /// <summary>
        /// Whether or not the user wants to add another item after this one.
        /// </summary>
        public bool AddAgain => _blnAddAgain;

        /// <summary>
        /// Whether or not the selected Vehicle is used.
        /// </summary>
        public bool BlackMarketDiscount => _blnBlackMarketDiscount;

        /// <summary>
        /// Armor that was selected in the dialogue.
        /// </summary>
        public string SelectedArmor => _strSelectedArmor;

        /// <summary>
        /// Whether or not the item should be added for free.
        /// </summary>
        public bool FreeCost => chkFreeItem.Checked;

        /// <summary>
        /// Markup percentage.
        /// </summary>
        public decimal Markup => _decMarkup;


        /// <summary>
        /// Markup percentage.
        /// </summary>
        public int Rating => _intRating;

        #endregion

        #region Methods
        /// <summary>
        /// Refreshes the displayed lists
        /// </summary>
        private void RefreshList()
        {
            string strFilter = "(" + _objCharacter.Options.BookXPath() + ')';

            string strCategory = cboCategory.SelectedValue?.ToString();
            if (!string.IsNullOrEmpty(strCategory) && strCategory != "Show All" && (_objCharacter.Options.SearchInCategoryOnly || txtSearch.TextLength == 0))
                strFilter += " and category = \"" + strCategory + '\"';
            else
            {
                StringBuilder objCategoryFilter = new StringBuilder();
                foreach (string strItem in _lstCategory.Select(x => x.Value))
                {
                    if (!string.IsNullOrEmpty(strItem))
                        objCategoryFilter.Append("category = \"" + strItem + "\" or ");
                }
                if (objCategoryFilter.Length > 0)
                {
                    strFilter += " and (" + objCategoryFilter.ToString().TrimEnd(" or ") + ')';
                }
            }

            if (txtSearch.TextLength != 0)
            {
                // Treat everything as being uppercase so the search is case-insensitive.
                string strSearchText = txtSearch.Text.ToUpper();
                strFilter += " and ((contains(translate(name,'abcdefghijklmnopqrstuvwxyzàáâãäåçèéêëìíîïñòóôõöùúûüýß','ABCDEFGHIJKLMNOPQRSTUVWXYZÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝß'), \"" + strSearchText + "\") and not(translate)) or contains(translate(translate,'abcdefghijklmnopqrstuvwxyzàáâãäåçèéêëìíîïñòóôõöùúûüýß','ABCDEFGHIJKLMNOPQRSTUVWXYZÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝß'), \"" + strSearchText + "\"))";
            }

            XmlNodeList objXmlArmorList = _objXmlDocument.SelectNodes("/chummer/armors/armor[" + strFilter + ']');
            BuildArmorList(objXmlArmorList);
        }

        /// <summary>
        /// Builds the list of Armors to render in the active tab.
        /// </summary>
        /// <param name="objXmlArmorList">XmlNodeList of Armors to render.</param>
        private void BuildArmorList(XmlNodeList objXmlArmorList)
        {
            switch (tabControl.SelectedIndex)
            {
                case 1:
                    DataTable tabArmor = new DataTable("armor");
                    tabArmor.Columns.Add("ArmorGuid");
                    tabArmor.Columns.Add("ArmorName");
                    tabArmor.Columns.Add("Armor");
                    tabArmor.Columns["Armor"].DataType = typeof(Int32);
                    tabArmor.Columns.Add("Capacity");
                    tabArmor.Columns["Capacity"].DataType = typeof(Decimal);
                    tabArmor.Columns.Add("Avail");
                    tabArmor.Columns.Add("Special");
                    tabArmor.Columns.Add("Source");
                    tabArmor.Columns.Add("Cost");

                    // Populate the Armor list.
                    foreach (XmlNode objXmlArmor in objXmlArmorList)
                    {
                        if (!chkHideOverAvailLimit.Checked || Backend.SelectionShared.CheckAvailRestriction(objXmlArmor, _objCharacter))
                        {
                            Armor objArmor = new Armor(_objCharacter);
                            List<Weapon> lstWeapons = new List<Weapon>();
                            objArmor.Create(objXmlArmor, 0, lstWeapons, true, true, true);

                            string strArmorGuid = objArmor.SourceID.ToString("D");
                            string strArmorName = objArmor.DisplayName(GlobalOptions.Language);
                            int intArmor = objArmor.TotalArmor;
                            decimal decCapacity = Convert.ToDecimal(objArmor.CalculatedCapacity, GlobalOptions.CultureInfo);
                            string strAvail = objArmor.Avail;
                            StringBuilder strAccessories = new StringBuilder();
                            foreach (ArmorMod objMod in objArmor.ArmorMods)
                            {
                                strAccessories.Append(objMod.DisplayName(GlobalOptions.Language));
                                strAccessories.Append('\n');
                            }
                            foreach (Gear objGear in objArmor.Gear)
                            {
                                strAccessories.Append(objGear.DisplayName(GlobalOptions.Language));
                                strAccessories.Append('\n');
                            }
                            if (strAccessories.Length > 0)
                                strAccessories.Length -= 1;
                            string strSource = objArmor.Source + ' ' + objArmor.Page(GlobalOptions.Language);
                            string strCost = objArmor.DisplayCost(out decimal decDummy, false);

                            tabArmor.Rows.Add(strArmorGuid, strArmorName, intArmor, decCapacity, strAvail, strAccessories.ToString(), strSource, strCost);
                        }
                    }

                    DataSet set = new DataSet("armor");
                    set.Tables.Add(tabArmor);

                    dgvArmor.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
                    dgvArmor.DataSource = set;
                    dgvArmor.DataMember = "armor";
                    break;
                default:
                    List<ListItem> lstArmors = new List<ListItem>();
                    foreach (XmlNode objXmlArmor in objXmlArmorList)
                    {
                        if (!chkHideOverAvailLimit.Checked || Backend.SelectionShared.CheckAvailRestriction(objXmlArmor, _objCharacter))
                        {
                            string strDisplayName = objXmlArmor["translate"]?.InnerText ?? objXmlArmor["name"]?.InnerText;
                            if (!_objCharacter.Options.SearchInCategoryOnly && txtSearch.TextLength != 0)
                            {
                                string strCategory = objXmlArmor["category"]?.InnerText;
                                if (!string.IsNullOrEmpty(strCategory))
                                {
                                    ListItem objFoundItem = _lstCategory.Find(objFind => objFind.Value == strCategory);
                                    if (!string.IsNullOrEmpty(objFoundItem.Name))
                                    {
                                        strDisplayName += " [" + objFoundItem.Name + "]";
                                    }
                                }
                            }

                            lstArmors.Add(new ListItem(objXmlArmor["id"]?.InnerText, strDisplayName));
                        }
                    }
                    lstArmors.Sort(CompareListItems.CompareNames);
                    lstArmor.BeginUpdate();
                    lstArmor.DataSource = null;
                    lstArmor.ValueMember = "Value";
                    lstArmor.DisplayMember = "Name";
                    lstArmor.DataSource = lstArmors;
                    lstArmor.EndUpdate();
                    break;
            }
        }
        /// <summary>
        /// Accept the selected item and close the form.
        /// </summary>
        private void AcceptForm()
        {
            XmlNode objNode = null;
            switch (tabControl.SelectedIndex)
            {
                case 0:
                    objNode =
                        _objXmlDocument.SelectSingleNode("/chummer/armors/armor[id = \"" + lstArmor.SelectedValue + "\"]");
                    break;
                case 1:
                    objNode =
                        _objXmlDocument.SelectSingleNode("/chummer/armors/armor[id = \"" +
                                                         dgvArmor.SelectedRows[0].Cells[0].Value + "\"]");
                    break;
            }
            if (objNode != null)
            {
                _strSelectCategory = (_objCharacter.Options.SearchInCategoryOnly || txtSearch.TextLength == 0) ? cboCategory.SelectedValue?.ToString() : objNode["category"]?.InnerText;
                _strSelectedArmor = objNode["name"]?.InnerText;
                _decMarkup = nudMarkup.Value;
                _intRating = decimal.ToInt32(nudRating.Value);
                _blnBlackMarketDiscount = chkBlackMarketDiscount.Checked;

                DialogResult = DialogResult.OK;
            }
        }

        private void MoveControls()
        {
            int intWidth = lblArmorLabel.Width;
            intWidth = Math.Max(intWidth, lblCapacityLabel.Width);
            intWidth = Math.Max(intWidth, lblAvailLabel.Width);
            intWidth = Math.Max(intWidth, lblCostLabel.Width);

            lblArmor.Left = lblArmorLabel.Left + intWidth + 6;
            lblCapacity.Left = lblCapacityLabel.Left + intWidth + 6;
            lblAvail.Left = lblAvailLabel.Left + intWidth + 6;
            lblTestLabel.Left = lblAvail.Left + lblAvail.Width + 16;
            lblCost.Left = lblCostLabel.Left + intWidth + 6;

            nudMarkup.Left = lblMarkupLabel.Left + lblMarkupLabel.Width + 6;
            lblMarkupPercentLabel.Left = nudMarkup.Left + nudMarkup.Width;

            lblSource.Left = lblSourceLabel.Left + lblSourceLabel.Width + 6;

            lblSearchLabel.Left = txtSearch.Left - 6 - lblSearchLabel.Width;
        }

        private void UpdateArmorInfo()
        {
            chkBlackMarketDiscount.Checked = _setBlackMarketMaps.Contains(_objSelectedArmor.Category);
            _objSelectedArmor.DiscountCost = chkBlackMarketDiscount.Checked;
            _objSelectedArmor.Rating = decimal.ToInt32(nudRating.Value);

            lblArmor.Text = _objSelectedArmor.DisplayName(GlobalOptions.Language);
            string strBook = CommonFunctions.LanguageBookShort(_objSelectedArmor.Source, GlobalOptions.Language);
            string strPage = _objSelectedArmor.Page(GlobalOptions.Language);

            tipTooltip.SetToolTip(lblSource,
                CommonFunctions.LanguageBookLong(_objSelectedArmor.Source, GlobalOptions.Language) + ' ' +
                LanguageManager.GetString("String_Page", GlobalOptions.Language) + ' ' + strPage);

            lblArmorValue.Text = _objSelectedArmor.DisplayArmorValue;
            lblCapacity.Text = _objSelectedArmor.CalculatedCapacity;

            decimal decItemCost = 0;
            if (chkFreeItem.Checked)
            {
                lblCost.Text = 0.ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + '¥';
            }
            else
            {
                lblCost.Text = _objSelectedArmor.DisplayCost(out decItemCost, true, nudMarkup.Value / 100.0m);
            }

            string strTotalAvail = _objSelectedArmor.TotalAvail(GlobalOptions.Language);
            lblAvail.Text = strTotalAvail;
            lblTest.Text = _objCharacter.AvailTest(decItemCost, strTotalAvail);
        }
        #endregion
    }
}
