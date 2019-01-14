﻿//********************************************************************************************
//Author: Sergey Stoyan
//        sergey.stoyan@gmail.com
//        http://www.cliversoft.com
//********************************************************************************************
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;

namespace Cliver.PdfDocumentParser
{
    /// <summary>
    /// template editor GUI
    /// </summary>
    public partial class TemplateForm : Form
    {
        PageCollection pages = null;

        void reloadPageBitmaps()
        {
            if (pages == null)
                return;
            pages.Clear();
            pages.ActiveTemplate = getTemplateFromUI(false);
            showPage(currentPage);
        }

        void setScaledImage()
        {
            if (pages == null)
                return;
            if (scaledCurrentPageBitmap != null)
                scaledCurrentPageBitmap.Dispose();
            if(pages[currentPage].ActiveTemplateBitmap == null)
                pages.ActiveTemplate = getTemplateFromUI(false);
            scaledCurrentPageBitmap = ImageRoutines.GetScaled(pages[currentPage].ActiveTemplateBitmap, (float)pictureScale.Value * Settings.Constants.Image2PdfResolutionRatio);
            if (picture.Image != null)
                picture.Image.Dispose();
            picture.Image = new Bitmap(scaledCurrentPageBitmap);
        }
        Bitmap scaledCurrentPageBitmap;

        PointF? findAndDrawAnchor(int anchorId)
        {
            Template.Anchor a = getAnchor(anchorId, out DataGridViewRow row);
            if (a == null || row == null)
                throw new Exception("Anchor[Id=" + anchorId + "] does not exist.");

            if (pages == null)
                return null;

            pages.ActiveTemplate = getTemplateFromUI(false);
            a = pages.ActiveTemplate.Anchors.FirstOrDefault(x => x.Id == anchorId);
            if (a == null)
                throw new Exception("Anchor[Id=" + a.Id + "] is not defined.");

            bool set = true;
            for (Template.Anchor a_ = a; a_ != null; a_ = pages.ActiveTemplate.Anchors.FirstOrDefault(x => x.Id == a_.ParentAnchorId))
            {
                if (a != a_)
                    showAnchorRowAs(a_.Id, rowStates.Parent, false);
                if (!a_.IsSet())
                {
                    set = false;
                    getAnchor(a_.Id, out DataGridViewRow r_);
                    setRowStatus(statuses.WARNING, r_, "Not set");
                }
            }
            if (!set)
                return null;
            List<List<RectangleF>> rss = pages[currentPage].GetAnchorRectangless(a.Id);
            getAnchor(a.Id, out DataGridViewRow r);
            if (rss == null || rss.Count < 1)
            {
                setRowStatus(statuses.ERROR, r, "Not found");
                return null;
            }
            setRowStatus(statuses.SUCCESS, r, "Found");

            PointF? p0 = null;
            for (int i = rss.Count - 1; i >= 0; i--)
            {
                List<RectangleF> rs = rss[i];
                drawBoxes(Settings.Appearance.AnchorMasterBoxColor, Settings.Appearance.AnchorMasterBoxBorderWidth, new List<System.Drawing.RectangleF> { rs[0] });
                if (rs.Count > 1)
                    drawBoxes(Settings.Appearance.AnchorSecondaryBoxColor, Settings.Appearance.AnchorSecondaryBoxBorderWidth, rs.GetRange(1, rs.Count - 1));

                if (i == rss.Count - 1)
                    p0 = new PointF(rs[0].X, rs[0].Y);
            }
            return p0;
        }

        object extractFieldAndDrawSelectionBox(Template.Field field)
        {
            try
            {
                if (pages == null)
                    return null;

                if (field.Rectangle == null)
                    return null;

                pages.ActiveTemplate = getTemplateFromUI(false);

                RectangleF r = field.Rectangle.GetSystemRectangleF();
                RectangleF r0 = r;
                if (field.LeftAnchorId != null)
                {
                    findAndDrawAnchor((int)field.LeftAnchorId);
                    Page.AnchorActualInfo aai = pages[currentPage].GetAnchorActualInfo((int)field.LeftAnchorId);
                    if (!aai.Found)
                        return null;
                    r.X += aai.Shift.Width;
                }
                if (field.TopAnchorId != null)
                {
                    findAndDrawAnchor((int)field.TopAnchorId);
                    Page.AnchorActualInfo aai = pages[currentPage].GetAnchorActualInfo((int)field.TopAnchorId);
                    if (!aai.Found)
                        return null;
                    r.Y += aai.Shift.Height;
                }
                if (field.RightAnchorId != null)
                {
                    findAndDrawAnchor((int)field.RightAnchorId);
                    Page.AnchorActualInfo aai = pages[currentPage].GetAnchorActualInfo((int)field.RightAnchorId);
                    if (!aai.Found)
                        return null;
                    r.Width += r0.X - r.X + aai.Shift.Width;
                }
                if (field.BottomAnchorId != null)
                {
                    findAndDrawAnchor((int)field.BottomAnchorId);
                    Page.AnchorActualInfo aai = pages[currentPage].GetAnchorActualInfo((int)field.BottomAnchorId);
                    if (!aai.Found)
                        return null;
                    r.Height += r0.Y - r.Y + aai.Shift.Height;
                }
                drawBoxes(Settings.Appearance.SelectionBoxColor, Settings.Appearance.SelectionBoxBorderWidth, new List<RectangleF> { r });
                switch (field.Type)
                {
                    case Template.Types.PdfText:
                        return Page.NormalizeText(Pdf.GetTextByTopLeftCoordinates(pages[currentPage].PdfCharBoxs, r, pages.ActiveTemplate.TextAutoInsertSpaceThreshold));
                    case Template.Types.OcrText:
                        return Page.NormalizeText(Ocr.This.GetText(pages[currentPage].ActiveTemplateBitmap, r));
                    case Template.Types.ImageData:
                        using (Bitmap rb = pages[currentPage].GetRectangleFromActiveTemplateBitmap(r.X / Settings.Constants.Image2PdfResolutionRatio, r.Y / Settings.Constants.Image2PdfResolutionRatio, r.Width / Settings.Constants.Image2PdfResolutionRatio, r.Height / Settings.Constants.Image2PdfResolutionRatio))
                        {
                            return ImageData.GetScaled(rb, Settings.Constants.Image2PdfResolutionRatio);
                        }
                    default:
                        throw new Exception("Unknown option: " + field.Type);
                }
            }
            catch (Exception ex)
            {
                //LogMessage.Error("Rectangle", ex);
                LogMessage.Error(ex);
            }
            return null;
        }

        void clearImageFromBoxes()
        {
            picture.Image?.Dispose();
            if (scaledCurrentPageBitmap != null)
                picture.Image = new Bitmap(scaledCurrentPageBitmap);
            drawnAnchorIds.Clear();
        }
        readonly HashSet<int> drawnAnchorIds = new HashSet<int>();

        void drawBoxes(Color color, float borderWidth, IEnumerable<System.Drawing.RectangleF> rs)
        {
            if (pages == null)
                return;

            Bitmap bm = new Bitmap(picture.Image);
            using (Graphics gr = Graphics.FromImage(bm))
            {
                float factor = (float)pictureScale.Value;
                Pen p = new Pen(color, borderWidth);
                p.Alignment = System.Drawing.Drawing2D.PenAlignment.Outset;
                foreach (System.Drawing.RectangleF r in rs)
                {
                    System.Drawing.Rectangle r_ = new System.Drawing.Rectangle((int)(r.X * factor), (int)(r.Y * factor), (int)(r.Width * factor), (int)(r.Height * factor));
                    //if (invertColor)
                    //{
                    //    for (int i = r_.X; i <= r_.X + r_.Width; i++)
                    //        for (int j = r_.Y; j <= r_.Y + r_.Height; j++)
                    //        {
                    //            Color rgb = bm.GetPixel(i, j);
                    //            rgb = Color.FromArgb(255 - rgb.R, 255 - rgb.G, 255 - rgb.B);
                    //            bm.SetPixel(i, j, rgb);
                    //        }
                    //}
                    gr.DrawRectangle(p, r_);
                }
            }
            if (picture.Image != null)
                picture.Image.Dispose();
            picture.Image = bm;
        }
        Point selectionBoxPoint0, selectionBoxPoint1, selectionBoxPoint2;
        bool drawingSelectionBox = false;

        void showPage(int page_i)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(testFile.Text) || 0 >= page_i || totalPageNumber < page_i)
                    return;

                foreach (DataGridViewRow r in fields.Rows)
                    setFieldRowValue(r, true);

                currentPage = page_i;
                tCurrentPage.Text = currentPage.ToString();

                setScaledImage();
                enableNavigationButtons();

                anchors.CurrentCell = null;//1-st row is autoselected
                conditions.CurrentCell = null;//1-st row is autoselected
                fields.CurrentCell = null;//1-st row is autoselected
                anchors.ClearSelection();//1-st row is autoselected
                conditions.ClearSelection();//1-st row is autoselected
                fields.ClearSelection();//1-st row is autoselected
                //setCurrentAnchorRow(null, true);
                //setCurrentConditionRow(null);
                //setCurrentFieldRow(null);
                loadingTemplate = false;

                if (ExtractFieldsAutomaticallyWhenPageChanged.Checked)
                {
                    foreach (DataGridViewRow row in fields.Rows)
                        setFieldRowValue(row, false);
                }

                if (CheckConditionsAutomaticallyWhenPageChanged.Checked)
                    setConditionsStatus();
            }
            catch (Exception e)
            {
                LogMessage.Error(e);
            }
        }
        int currentPage;
        int totalPageNumber;

        private void bPrevPage_Click(object sender, EventArgs e)
        {
            showPage(currentPage - 1);
        }

        private void bNextPage_Click(object sender, EventArgs e)
        {
            showPage(currentPage + 1);
        }

        void enableNavigationButtons()
        {
            bPrevPage.Enabled = currentPage > 1;
            bNextPage.Enabled = currentPage < totalPageNumber;
        }

        private void tCurrentPage_Leave(object sender, EventArgs e)
        {
            changeCurrentPage();
        }

        private void changeCurrentPage()
        {
            if (int.TryParse(tCurrentPage.Text, out int i))
            {
                if (i != currentPage)
                    showPage(i);
            }
            else
            {
                LogMessage.Error("Page is not a number.");
                tCurrentPage.Text = currentPage.ToString();
            }
        }

        private void tCurrentPage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                changeCurrentPage();
        }
    }
}