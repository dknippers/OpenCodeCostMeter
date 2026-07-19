#include "services.h"
#include "widget_window.h"
#include <QApplication>
#include <QCommandLineParser>
#include <QFontDatabase>
#include <QMessageBox>
#include <QMenu>
#include <QSystemTrayIcon>

int main(int argc, char *argv[]) {
    QApplication app(argc, argv); app.setApplicationName("OpenCode Cost Meter"); app.setQuitOnLastWindowClosed(false);
    QFontDatabase::addApplicationFont(":/fonts/CascadiaMono.ttf");
    QFontDatabase::addApplicationFont(":/fonts/Inter-Regular.ttf");
    QCommandLineParser parser; parser.setApplicationDescription("Displays today's OpenCode LLM spend."); parser.addHelpOption(); QCommandLineOption databaseOption("db-path", "Use an alternative opencode.db location.", "path"); parser.addOption(databaseOption); parser.process(app);
    const QString database = DbLocator::resolve(parser.value(databaseOption)); if (database.isEmpty()) { QMessageBox::critical(nullptr, "OpenCode Cost Meter", "Could not find the opencode database.\n\nUse --db-path <path> to specify it."); return 1; }
    SettingsStore store; Settings settings = store.load(); UsagePoller poller(database, settings.pollIntervalSeconds); WidgetWindow window(settings, store, poller); QSystemTrayIcon tray(QIcon(":/icon.svg"), &app); QMenu menu; menu.addAction("Exit", &app, &QCoreApplication::quit); if (QSystemTrayIcon::isSystemTrayAvailable()) { tray.setToolTip("OpenCode Cost Meter"); tray.setContextMenu(&menu); QObject::connect(&tray, &QSystemTrayIcon::activated, &window, [&window](auto) { window.isVisible() ? window.hide() : window.show(); }); tray.show(); } poller.start(); const int result = app.exec(); store.save(settings); return result;
}
