#include "widget_window.h"
#include <QApplication>
#include <QCloseEvent>
#include <QContextMenuEvent>
#include <QEnterEvent>
#include <QGuiApplication>
#include <QHBoxLayout>
#include <QKeyEvent>
#include <QLabel>
#include <QLocale>
#include <QMenu>
#include <QMouseEvent>
#include <QScreen>
#include <QScrollArea>
#include <QSlider>
#include <QSet>
#include <QStyle>
#include <QVBoxLayout>
#include <QWidgetAction>
#include <functional>

namespace { constexpr auto Accent = "#61DBB4"; constexpr auto Text = "#E5E2E1"; constexpr auto Muted = "#ADABAA"; }
class MenuRow final : public QWidget {
public:
    MenuRow(QMenu *menu, const QString &text, const QString &shortcut, bool checked, std::function<void()> clicked)
        : QWidget(menu), m_menu(menu), m_clicked(std::move(clicked)) {
        setFixedSize(192, 32);
        auto *layout = new QHBoxLayout(this); layout->setContentsMargins(4, 0, 16, 0); layout->setSpacing(4);
        auto *indicator = new QLabel(checked ? QStringLiteral("\u2022") : QString(), this); indicator->setFixedWidth(12); indicator->setAlignment(Qt::AlignCenter); indicator->setStyleSheet(QString("color:%1;font-size:16px;font-weight:bold;").arg(Accent));
        auto *label = new QLabel(text, this); label->setStyleSheet(QString("color:%1;font-family:Inter;font-size:12px;font-weight:500;").arg(Text));
        auto *key = new QLabel(shortcut, this); key->setStyleSheet(QString("color:%1;font-family:Inter;font-size:12px;font-weight:500;").arg(Muted));
        layout->addWidget(indicator); layout->addWidget(label, 1); layout->addWidget(key);
    }
protected:
    void enterEvent(QEnterEvent *event) override { setStyleSheet("background:#2A2A2A;"); QWidget::enterEvent(event); }
    void leaveEvent(QEvent *event) override { setStyleSheet("background:transparent;"); QWidget::leaveEvent(event); }
    void mouseReleaseEvent(QMouseEvent *event) override { if (event->button() == Qt::LeftButton && rect().contains(event->position().toPoint())) { m_menu->close(); m_clicked(); } }
private:
    QMenu *m_menu; std::function<void()> m_clicked;
};
class ModelRowWidget final : public QWidget {
public:
    ModelRowWidget(const ModelBreakdown &model, QWidget *parent) : QWidget(parent) { auto *layout = new QHBoxLayout(this); layout->setContentsMargins(0, 12, 0, 0); layout->setSpacing(16); name = new QLabel(ModelDisplayNameRules::format(model.model), this); cost = new QLabel(this); name->setStyleSheet(QString("color:%1;").arg(Text)); cost->setStyleSheet(QString("color:%1;").arg(Muted)); layout->addWidget(name, 1); layout->addWidget(cost); setCost(model.cost, false); }
    void setCost(double value, bool highlight) { cost->setText(QLocale(QLocale::English, QLocale::UnitedStates).toCurrencyString(value, "$", 3)); cost->setStyleSheet(QString("color:%1;").arg(highlight ? Accent : Muted)); }
    void clearHighlight() { cost->setStyleSheet(QString("color:%1;").arg(Muted)); }
private: QLabel *name; QLabel *cost;
};
WidgetWindow::WidgetWindow(Settings &settings, SettingsStore &store, UsagePoller &poller) : m_settings(settings), m_store(store), m_poller(poller) {
    setWindowFlags(Qt::FramelessWindowHint | Qt::Tool | (settings.alwaysOnTop ? Qt::WindowStaysOnTopHint : Qt::WindowFlags())); setAttribute(Qt::WA_TranslucentBackground); setWindowOpacity(qBound(0.05, settings.opacity, 1.0)); setFocusPolicy(Qt::StrongFocus);
    auto *card = new QWidget(this); card->setObjectName("card"); m_cardLayout = new QVBoxLayout(card); m_cardLayout->setContentsMargins(settings.isExpanded ? 18 : 12, settings.isExpanded ? 12 : 8, settings.isExpanded ? 18 : 12, settings.isExpanded ? 12 : 8); m_cardLayout->setSpacing(0); m_total = new QLabel("$0.00", card); m_total->setAlignment(Qt::AlignCenter); m_total->setObjectName("total"); m_empty = new QLabel("no usage yet", card); m_empty->setAlignment(Qt::AlignCenter); m_empty->setStyleSheet(QString("color:%1;").arg(Muted)); m_empty->hide(); m_error = new QLabel(card); m_error->setWordWrap(true); m_error->setStyleSheet("color:#E0B341;"); m_error->hide(); m_rowsHost = new QWidget(card); m_rowsHost->setAutoFillBackground(false); m_rowsLayout = new QVBoxLayout(m_rowsHost); m_rowsLayout->setContentsMargins(0, 0, 0, 0); m_rowsLayout->setSpacing(0); m_scroll = new QScrollArea(card); m_scroll->setObjectName("rowsScroll"); m_scroll->setWidget(m_rowsHost); m_scroll->setWidgetResizable(false); m_scroll->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff); m_scroll->setMaximumHeight(320); m_scroll->setFrameShape(QFrame::NoFrame); m_scroll->viewport()->setAutoFillBackground(false); m_scroll->hide(); m_cardLayout->addWidget(m_total); m_cardLayout->addWidget(m_empty); m_cardLayout->addWidget(m_scroll); m_cardLayout->addWidget(m_error);
    auto *root = new QVBoxLayout(this); root->setContentsMargins(0, 0, 0, 0); root->setSizeConstraint(QLayout::SetFixedSize); root->addWidget(card); setStyleSheet("#card { background:#20201F; border:1px solid #4A4A4A; border-radius:4px; } #rowsScroll, #rowsScroll > QWidget > QWidget { background:transparent; } QLabel { font-family:'Cascadia Mono'; font-size:14px; } QMenu, QSlider { font-family:Inter; } #total { font-size:24px; font-weight:bold; color:#E5E2E1; } QMenu { background:#20201F; color:#E5E2E1; border:1px solid #4A4A4A; } QMenu::item:selected { background:#2A2A2A; }");
    m_highlightTimer.setSingleShot(true); connect(&m_highlightTimer, &QTimer::timeout, this, [this] { m_total->setStyleSheet("color:#E5E2E1;"); for (auto *row : m_rows) row->clearHighlight(); }); m_saveTimer.setSingleShot(true); m_saveTimer.setInterval(500); connect(&m_saveTimer, &QTimer::timeout, this, [this] { m_store.save(m_settings); }); connect(&m_poller, &UsagePoller::updated, this, &WidgetWindow::applySnapshot); connect(&m_poller, &UsagePoller::failed, this, &WidgetWindow::showError);
    if (!qIsNaN(settings.x) && !qIsNaN(settings.y)) move(qRound(settings.x), qRound(settings.y)); else { const QRect r = screen()->availableGeometry(); move(r.center() - rect().center()); }
    m_scroll->setVisible(settings.isExpanded);
}
void WidgetWindow::applySnapshot(const DayUsageSnapshot &snapshot) { m_error->hide(); m_total->show(); const QString total = QLocale(QLocale::English, QLocale::UnitedStates).toCurrencyString(snapshot.cost, "$", 2); const bool changed = !m_firstUpdate && total != m_total->text(); m_total->setText(total); m_total->setStyleSheet(changed ? QString("color:%1;").arg(Accent) : QString("color:%1;").arg(Text)); updateRows(snapshot.models); if (changed) m_highlightTimer.start(2000); if (m_firstUpdate) { m_firstUpdate = false; show(); clampToScreen(); } }
void WidgetWindow::showError(const QString &error) { m_total->hide(); m_scroll->hide(); m_empty->hide(); m_error->setText(error); m_error->show(); if (m_firstUpdate) { m_firstUpdate = false; show(); clampToScreen(); } }
QString WidgetWindow::modelKey(const ModelBreakdown &m) const { return m.provider.isEmpty() ? m.model : m.provider + '/' + m.model; }
void WidgetWindow::updateRows(const QList<ModelBreakdown> &models) { QSet<QString> wanted; bool anyHighlight = false; for (const auto &model : models) { const QString key = modelKey(model); const QString value = QLocale(QLocale::English, QLocale::UnitedStates).toCurrencyString(model.cost, "$", 3); const bool highlight = m_previousCosts.contains(key) && m_previousCosts.value(key) != value; m_previousCosts.insert(key, value); if (model.cost < 0.0005) continue; wanted.insert(key); auto *row = m_rows.value(key); if (!row) { row = new ModelRowWidget(model, m_rowsHost); m_rows.insert(key, row); m_rowsLayout->addWidget(row); } row->setCost(model.cost, highlight); anyHighlight |= highlight; }
    for (auto it = m_rows.begin(); it != m_rows.end();) { if (!wanted.contains(it.key())) { delete it.value(); it = m_rows.erase(it); } else ++it; } m_rowsLayout->activate(); const QSize contentSize = m_rowsLayout->sizeHint(); const bool needsScrollBar = contentSize.height() > m_scroll->maximumHeight(); const int scrollBarWidth = needsScrollBar ? style()->pixelMetric(QStyle::PM_ScrollBarExtent) : 0; m_rowsHost->setFixedSize(contentSize); m_scroll->setFixedSize(contentSize.width() + scrollBarWidth, qMin(contentSize.height(), m_scroll->maximumHeight())); m_empty->setVisible(m_settings.isExpanded && m_rows.isEmpty()); m_scroll->setVisible(m_settings.isExpanded && !m_rows.isEmpty()); if (anyHighlight) m_highlightTimer.start(2000); }
void WidgetWindow::toggleExpanded() { m_settings.isExpanded = !m_settings.isExpanded; const int horizontal = m_settings.isExpanded ? 18 : 12; const int vertical = m_settings.isExpanded ? 12 : 8; m_cardLayout->setContentsMargins(horizontal, vertical, horizontal, vertical); m_scroll->setVisible(m_settings.isExpanded && !m_rows.isEmpty()); m_empty->setVisible(m_settings.isExpanded && m_rows.isEmpty()); adjustSize(); saveSoon(); }
void WidgetWindow::saveSoon() { m_settings.x = x(); m_settings.y = y(); m_saveTimer.start(); }
void WidgetWindow::clampToScreen() { QScreen *s = QGuiApplication::screenAt(frameGeometry().center()); if (!s) s = screen(); const QRect r = s->availableGeometry(); move(qBound(r.left(), x(), qMax(r.left(), r.right() - width() + 1)), qBound(r.top(), y(), qMax(r.top(), r.bottom() - height() + 1))); }
void WidgetWindow::center(bool horizontal) { QScreen *target = QGuiApplication::screenAt(frameGeometry().center()); if (!target) target = screen(); const QRect r = target->availableGeometry(); m_previousResizeAnchor.reset(); if (horizontal) move(r.x() + (r.width() - width()) / 2, y()); else move(x(), r.y() + (r.height() - height()) / 2); saveSoon(); }
void WidgetWindow::mousePressEvent(QMouseEvent *event) { if (event->button() == Qt::LeftButton) { m_dragStart = event->globalPosition().toPoint(); m_dragging = false; } QWidget::mousePressEvent(event); }
void WidgetWindow::mouseMoveEvent(QMouseEvent *event) { if (event->buttons() & Qt::LeftButton && (m_dragging || (event->globalPosition().toPoint() - m_dragStart).manhattanLength() >= QApplication::startDragDistance())) { move(pos() + event->globalPosition().toPoint() - m_dragStart); m_dragStart = event->globalPosition().toPoint(); m_dragging = true; m_previousResizeAnchor.reset(); } }
void WidgetWindow::mouseReleaseEvent(QMouseEvent *event) { if (event->button() == Qt::LeftButton) { if (!m_dragging) toggleExpanded(); else { clampToScreen(); saveSoon(); } } }
void WidgetWindow::contextMenuEvent(QContextMenuEvent *event) { buildMenu(event->globalPos()); }
void WidgetWindow::buildMenu(const QPoint &at) {
    auto *menu = new QMenu(this); menu->setStyleSheet("QMenu { background:#20201F; border:1px solid #4A4A4A; padding:0; border-radius:0; } QSlider::groove:horizontal { background:#4A4A4A; height:4px; border-radius:2px; } QSlider::sub-page:horizontal { background:#61DBB4; border-radius:2px; } QSlider::handle:horizontal { background:#E5E2E1; border:1px solid #4A4A4A; width:12px; height:12px; margin:-4px 0; border-radius:6px; }");
    auto addWidget = [menu](QWidget *widget) { auto *action = new QWidgetAction(menu); action->setDefaultWidget(widget); menu->addAction(action); };
    auto addRow = [this, menu, &addWidget](const QString &text, const QString &shortcut, bool checked, auto clicked) { addWidget(new MenuRow(menu, text, shortcut, checked, clicked)); };
    auto addHeader = [menu, &addWidget](const QString &text) { auto *host = new QWidget(menu); host->setFixedSize(192, 32); auto *layout = new QHBoxLayout(host); layout->setContentsMargins(20, 0, 10, 0); auto *label = new QLabel(text, host); label->setStyleSheet(QString("color:%1;font-family:Inter;font-size:12px;font-weight:600;").arg(Text)); layout->addWidget(label); addWidget(host); };
    auto addSlider = [this, menu, &addWidget](int min, int max, int value, const QString &suffix, auto changed) {
        auto *host = new QWidget(menu); host->setFixedSize(192, 32); auto *layout = new QHBoxLayout(host); layout->setContentsMargins(20, 0, 10, 0); layout->setSpacing(10);
        auto *slider = new QSlider(Qt::Horizontal, host); slider->setFixedWidth(120); slider->setRange(min, max); slider->setSingleStep(5); slider->setPageStep(5); slider->setValue(value);
        auto *label = new QLabel(QString::number(value) + suffix, host); label->setFixedWidth(32); label->setStyleSheet(QString("color:%1;font-family:Inter;font-size:12px;font-weight:500;").arg(Text));
        layout->addWidget(slider); layout->addWidget(label); connect(slider, &QSlider::valueChanged, this, [slider, label, suffix, changed](int v) { const int snapped = qRound(v / 5.0) * 5; if (snapped != v) { slider->setValue(snapped); return; } label->setText(QString::number(v) + suffix); changed(v); }); addWidget(host);
    };
    addRow("Always on top", "A", m_settings.alwaysOnTop, [this] { m_settings.alwaysOnTop = !m_settings.alwaysOnTop; setWindowFlag(Qt::WindowStaysOnTopHint, m_settings.alwaysOnTop); show(); saveSoon(); });
    addHeader("Poll interval"); addSlider(5, 60, qRound(m_settings.pollIntervalSeconds), "s", [this](int v) { m_settings.pollIntervalSeconds = v; m_poller.setInterval(v); saveSoon(); });
    addHeader("Opacity"); addSlider(5, 100, qRound(m_settings.opacity * 100), "%", [this](int v) { m_settings.opacity = v / 100.0; setWindowOpacity(m_settings.opacity); saveSoon(); });
    addRow("Center horizontally", "H", false, [this] { center(true); }); addRow("Center vertically", "V", false, [this] { center(false); }); addRow("Hide", "", false, [this] { hide(); }); addRow("Exit", "", false, [this] { m_exitRequested = true; qApp->quit(); });
    menu->exec(at); menu->deleteLater();
}
void WidgetWindow::closeEvent(QCloseEvent *event) { if (!m_exitRequested) { event->ignore(); hide(); } else { m_settings.x = x(); m_settings.y = y(); m_store.save(m_settings); event->accept(); } }
int WidgetWindow::computeResizeAnchorFlags(const QRect &geometry, const QPoint &screenCenter) {
    const int right = geometry.x() + geometry.width(); const int bottom = geometry.y() + geometry.height(); int flags = 0; const bool spansX = geometry.left() <= screenCenter.x() && right >= screenCenter.x(); const bool spansY = geometry.top() <= screenCenter.y() && bottom >= screenCenter.y();
    if (spansX) flags |= SpansX; if (spansY) flags |= SpansY;
    const bool inLeft = geometry.left() < screenCenter.x(); const bool inRight = right > screenCenter.x(); const bool inTop = geometry.top() < screenCenter.y(); const bool inBottom = bottom > screenCenter.y();
    if (inLeft && inTop) flags |= TopLeftQuadrant; if (inRight && inTop) flags |= TopRightQuadrant; if (inRight && inBottom) flags |= BottomRightQuadrant; if (inLeft && inBottom) flags |= BottomLeftQuadrant; return flags;
}
void WidgetWindow::resizeEvent(QResizeEvent *event) {
    if (event->oldSize().isEmpty()) return; const QSize delta = event->size() - event->oldSize(); if (delta.isNull()) return;
    const QRect oldGeometry(pos(), event->oldSize()); QScreen *target = QGuiApplication::screenAt(oldGeometry.center()); if (!target) target = screen(); if (!target) return;
    int flags; if (m_previousResizeAnchor) { flags = *m_previousResizeAnchor; m_previousResizeAnchor.reset(); } else { flags = computeResizeAnchorFlags(oldGeometry, target->availableGeometry().center()); m_previousResizeAnchor = flags; }
    int nextX = x(); int nextY = y(); if (flags & SpansX) nextX -= qRound(delta.width() / 2.0); else if (flags & (TopRightQuadrant | BottomRightQuadrant)) nextX -= delta.width(); if (flags & SpansY) nextY -= qRound(delta.height() / 2.0); else if (flags & (BottomLeftQuadrant | BottomRightQuadrant)) nextY -= delta.height(); move(nextX, nextY);
}
void WidgetWindow::keyPressEvent(QKeyEvent *event) { if (event->key() == Qt::Key_T) toggleExpanded(); else if (event->key() == Qt::Key_H) center(true); else if (event->key() == Qt::Key_V) center(false); else if (event->key() == Qt::Key_A) { m_settings.alwaysOnTop = !m_settings.alwaysOnTop; setWindowFlag(Qt::WindowStaysOnTopHint, m_settings.alwaysOnTop); show(); saveSoon(); } else QWidget::keyPressEvent(event); }
