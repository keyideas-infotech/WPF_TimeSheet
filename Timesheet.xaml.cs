using specs.ams.AppraisalForms;
using specs.ams.Core;
using specs.ams.Proxy;
using specs.ams.Utils;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Windows.Controls;

namespace specs.ams.TimesheetForms {

/// <summary>
/// Interaction logic for Timesheet.xaml
/// </summary>
public partial class Timesheet : Window {

	public Timesheet(Window owner) {
		this.Owner = owner;
		InitializeComponent();
	}

	int nextDiaryId = -1;
	dsReferencesDataSet workCodes;
	dsReferencesDataSet expenseCodes;
	dsTimeSheetDataSet timeSheetDataSet = new dsTimeSheetDataSet();
	ttFileDropDownDataTable fileDropTable;

	private void Window_Loaded(object sender, RoutedEventArgs e) {
		windowsLogin.Text = AmsSystem.WindowsLogin;
		sDate.SelectedDate = DateTime.Now;
		eDate.SelectedDate = DateTime.Now;
		addCalcColumns(timeSheetDataSet);
		timeSheetDataGrid.ItemsSource = timeSheetDataSet.ttFileDiary;
		AmsSystem.AmsProxy.ReferencesFillByRefType("WorkCode", out workCodes);
		((ObjectDataProvider)this.FindResource("workCodes"))
			.ObjectInstance = workCodes.ttReferences.DefaultView;
		AmsSystem.AmsProxy.ReferencesFillByRefType("ExpenseCode", out expenseCodes);
		((ObjectDataProvider)this.FindResource("expenseCodes"))
			.ObjectInstance = expenseCodes.ttReferences.DefaultView;

		// Get some recent Files for the drop-down.
		AmsSystem.AmsProxy.FileDropDown(
			AmsSystem.AppraiserDefaultLocation, // appraiser's city code
			200, // max records to fetch
			out fileDropTable
			);
		fileDropTable.DefaultView.Sort = "FileName";
		((ObjectDataProvider)this.FindResource("fileComboValues"))
			.ObjectInstance = fileDropTable.DefaultView;

	}

	private void addCalcColumns(dsTimeSheetDataSet dset) {
		var ttDiary = dset.ttFileDiary;
		ttDiary.Columns.Add(
			"expr_FileName",
			typeof(String),
			"Max(Child(msFile).FileName)"
			);
		ttDiary.Columns.Add(
			"expr_FileNumber",
			typeof(String),
			"Max(Child(msFile).FileNumber)"
			);
		ttDiary.Columns.Add(
			"expr_FileType",
			typeof(String),
			"Max(Child(msFile).FileType)"
			);
		ttDiary.Columns.Add(
			"expr_NotInvoiced",
			typeof(Boolean),
			"InvoiceDate is null"
			);
		ttDiary.Columns.Add("extra_FileNumber", typeof(String));
	}

	private void calcExtraColumns(DataRow row) {
		// For some reason, if the file was selected from the ComboBox,
		// then the FileNumber text was not displayed if it was bound
		// to the expression column expr_FileNumber.
		// So, I'm making it much more explicit here.
		if (row == null)
			return;
		int fileid = row.Field<int>("FileID");
		var file = fileDropTable.FindByFileID(fileid);
		string filenum = file == null ? "" : file.FileNumber;
		row.SetField<String>("extra_FileNumber", filenum);
	}

	private void timeSheetSum_Click(object sender, RoutedEventArgs e) {
		SumForm();
	}
	private void CalcFieldChanged(object sender, EventArgs e) {
		SumForm();
	}

	private void SumForm() {
		decimal allhrs = 0;
		decimal billhrs = 0;
		decimal expenses = 0;
		foreach (dsTimeSheetDataSet.ttFileDiaryRow diary in timeSheetDataSet.ttFileDiary.Rows) {
			allhrs += diary.Hours;
			if (!diary.NonBillable)
				billhrs += diary.Hours;
			expenses += diary.Expense;
		}
		totHours.Text = allhrs.ToString();
		billableHours.Text = billhrs.ToString();
		totExpense.Text =  expenses.ToString("c");
	}

	private void applyButton_Click(object sender, RoutedEventArgs e) {
		save();
	}

	private void okButton_Click(object sender, RoutedEventArgs e) {
		// Move focus to the button, to force field validate and binding update of IsDefault button.
		((Button)sender).Focus();
		if (!save())
			return;
		Close();
	}

	private void fetchButton_Click(object sender, RoutedEventArgs e) {
		if (timeSheetDataSet.HasChanges()) {
			MessageBox.Show("Save your changes before fetching a new set of data.");
			return;
		}
		AmsSystem.AmsProxy.TimeSheetFill(
			AmsSystem.WindowsLogin, 
			(DateTime)(sDate.SelectedDate),
			(DateTime)(eDate.SelectedDate),
			"", // not filtered by city code
			out timeSheetDataSet
			);
		addCalcColumns(timeSheetDataSet);
		foreach (DataRow row in timeSheetDataSet.ttFile) {
			copyRowToFileDropTable(row);
		}
		foreach (DataRow row in timeSheetDataSet.ttFileDiary) {
			calcExtraColumns(row);
			row.AcceptChanges();  // extra columns don't count as data changes
		}
		timeSheetDataGrid.ItemsSource = timeSheetDataSet.ttFileDiary;
		SumForm();
	}

	private void addButton_Click(object sender, RoutedEventArgs e) {
		// Validate the dataset before adding any new rows.
		if (!save())
			return;
		dsTimeSheetDataSet newSet;
		string errString;
		AmsSystem.AmsProxy.TimeSheetNew(AmsSystem.WindowsLogin, out newSet, out errString);
		if (Utils.DataSetUtil.findAndShowErrors(errString, newSet))
			return;
		newSet.ttFileDiary.Rows[0]["FileDiaryID"] = nextDiaryId;
		timeSheetDataSet.Merge(newSet);

		// TODO Check if this is valid if the new row would sort into the middle of the grid's items.
		// TODO i.e: Is the newly added item always at count-1 ?
		int newRowNum = timeSheetDataGrid.Items.Count - 1;
		var item = timeSheetDataGrid.Items[newRowNum];
		timeSheetDataGrid.UpdateLayout();
		timeSheetDataGrid.ScrollIntoView(item);
		timeSheetDataGrid.SelectedItem = item;

		// Move focus to the first column in the selected row.
		timeSheetDataGrid.Focus();
		timeSheetDataGrid.CurrentCell = new DataGridCellInfo(item, timeSheetDataGrid.Columns[0]);
		
		--nextDiaryId;
	}

	private bool save() {
		// Move focus to force binding update.
		okButton.Focus();
		if (timeSheetDataSet == null || !timeSheetDataSet.HasChanges())
			return true;
		if (!validate())
			return false;
		String errMessage;
		try {
			AmsSystem.AmsProxy.TimeSheetSave(ref timeSheetDataSet, out errMessage);
		} catch (Exception ex) { errMessage = ex.Message; }
		bool hadErrors = Utils.DataSetUtil.findAndShowErrors(errMessage, timeSheetDataSet);
		if (!hadErrors) {
			// The extra columns seem to get cleared out on Save() of newly added rows.
			foreach (DataRow row in timeSheetDataSet.ttFileDiary.AsEnumerable().Where(it => it.RowState != DataRowState.Deleted)) {
				calcExtraColumns(row);
			}
			timeSheetDataSet.AcceptChanges();
		} else
			scrollToErrorRow();
		return !hadErrors;
	}

	private bool validate() {
		// This validation is done on the client rather than on the appserver because
		// dsTimeSheetDataSet is used by other forms, where this validation should not exist.
		// Look for *new* rows without work/expense code. There may be rows from other diary
		// types which should not have this validation.
		foreach (DataRowView drv in timeSheetDataSet.ttFileDiary.DefaultView) {
			if (	drv.Row.RowState == DataRowState.Added
				&&	drv.Row.Field<int>("ExpenseCode") < 1
				&&	drv.Row.Field<int>("WorkCode") < 1
				) {
				timeSheetDataGrid.UpdateLayout();
				timeSheetDataGrid.ScrollIntoView(drv);
				timeSheetDataGrid.SelectedItem = drv;
				MessageBox.Show("Timesheet entry must have an Expense Code or a Work Code.");
				return false;
			}
		}
		return true;
	}

	private void scrollToErrorRow() {
		foreach (DataRowView drv in timeSheetDataSet.ttFileDiary.DefaultView) {
			if (drv.Row.HasErrors) {
				timeSheetDataGrid.UpdateLayout();
				timeSheetDataGrid.ScrollIntoView(drv);
				timeSheetDataGrid.SelectedItem = drv;
				return;
			}
		}
	}

	private void fileButton_Click(object sender, RoutedEventArgs e) {
		var drv = timeSheetDataGrid.SelectedItem as DataRowView;
		var diary = drv.Row as dsTimeSheetDataSet.ttFileDiaryRow;
		var lookup = new FileLookup(this);
		var ttFile = timeSheetDataSet.ttFile;
		lookup.setFilesList(ttFile);
		lookup.ShowDialog();
		var file = lookup.SelectedFile;
		if (file == null || file.FileID == diary.FileID)
			return;
		copyRowToFileTable(file);
		copyRowToFileDropTable(file);
		diary.FileID = file.FileID;
		calcExtraColumns(diary);
	}

	private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
		// Move focus to force binding update.
		cancelButton.Focus();
		if (timeSheetDataSet == null || ! timeSheetDataSet.HasChanges())
			return;
		var dialog = new UnsavedChanges();
		dialog.ShowDialog();
		switch (dialog.Selection) {
			case UnsavedChanges.Result.SaveAndClose:
				if (!save())
					e.Cancel = true; // don't close if Save failed.
				break;
			case UnsavedChanges.Result.DiscardAndClose:
				break;
			case UnsavedChanges.Result.ReturnToForm:
				e.Cancel = true;
				break;
		}
	}

	// Copy the previous row's FileID.
	private void sameButton_Click(object sender, RoutedEventArgs e) {
		int index = timeSheetDataGrid.SelectedIndex;
		if (index < 1)
			return;
		var prevRow = timeSheetDataGrid.Items[index - 1] as DataRowView;
		var currRow = timeSheetDataGrid.SelectedItem as DataRowView;
		currRow["FileID"] = prevRow["FileID"];
		calcExtraColumns(currRow.Row);
	}

	/// <summary>
	/// We have to add fetched File rows to the fileDropTable
	/// for the FileName ComboBox to display correctly.
	/// </summary>
	private void copyRowToFileDropTable(DataRow row) {
		if (row == null)
			return;
		int fileid = row.Field<int>("FileID");
		if (fileDropTable.FindByFileID(fileid) == null)
			Utils.DataSetUtil.RowCopy(row, fileDropTable);
	}

	/// <summary>
	/// We have to add fetched File rows to the ttFile table
	/// for the File name and number to display correctly.
	/// </summary>
	private void copyRowToFileTable(DataRow row) {
		if (row == null)
			return;
		int fileid = row.Field<int>("FileID");
		var ttFile = timeSheetDataSet.ttFile;
		if (ttFile.FindByFileID(fileid) == null) {
			Utils.DataSetUtil.RowCopy(row, ttFile);
			// This row is not intended to be saved to the db.
			// We only need it for the file name and number.
			ttFile.FindByFileID(fileid).AcceptChanges();
		}
	}

	private void fileNameCombo_Changed(object sender, RoutedEventArgs e) {
		var drv = timeSheetDataGrid.SelectedItem as DataRowView;
		if (drv == null)
			return;
		var combo = (ComboBox)sender;
		if (combo.SelectedValue == null)
			return;
		calcExtraColumns(drv.Row);
	}

    private void TimeSheetReport_Click(object sender, RoutedEventArgs e) {
        Stimulsoft.Report.StiReport report = new Stimulsoft.Report.StiReport();
        report.RegData("File", timeSheetDataSet);
        report.Load("reports\\TimeSheet_TimeSheetReport__Time and Expense by File Number.mrt");
        report.ShowWithWpf();
    }

    private void ExpenseReport_Click(object sender, RoutedEventArgs e) {
        Stimulsoft.Report.StiReport report = new Stimulsoft.Report.StiReport();
        report.RegData("File", timeSheetDataSet);
        report.Load("reports\\TimeSheet_ExpenseReport__Expense Report.mrt");
        report.ShowWithWpf();
    }

    private void deleteButton_Click(object sender, RoutedEventArgs e) {
		var drv = timeSheetDataGrid.SelectedItem as DataRowView;
		if (drv == null)
			return;
		drv.Delete();
	}

}
}
