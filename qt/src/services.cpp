#include "services.h"
#include <QCoreApplication>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QJsonDocument>
#include <QJsonObject>
#include <QLocale>
#include <QSqlError>
#include <QSqlQuery>
#include <QStandardPaths>
#include <QTextStream>
#include <QTime>

QHash<QString, QString> ModelDisplayNameRules::cache;
QList<QPair<QString, QList<QPair<QString, QString>>>> ModelDisplayNameRules::rules;

QString DbLocator::defaultPath() { return QDir::home().filePath(".local/share/opencode/opencode.db"); }
QString DbLocator::resolve(const QString &overridePath) { const QString path = overridePath.isEmpty() ? defaultPath() : overridePath; return QFile::exists(path) ? path : QString(); }

QString SettingsStore::path() const { return QDir(QStandardPaths::writableLocation(QStandardPaths::AppConfigLocation)).filePath("OpenCodeCostMeter.settings.json"); }
Settings SettingsStore::load() {
    QString settingsPath = path();
    if (!QFile::exists(settingsPath)) {
        const QString legacyPath = QDir(QCoreApplication::applicationDirPath()).filePath("OpenCodeCostMeter.settings.json");
        if (QFile::exists(legacyPath)) settingsPath = legacyPath;
    }
    QFile file(settingsPath); if (!file.open(QIODevice::ReadOnly)) return {};
    const auto object = QJsonDocument::fromJson(file.readAll()).object(); Settings s;
    s.x = object.value("x").toDouble(qQNaN()); s.y = object.value("y").toDouble(qQNaN()); s.alwaysOnTop = object.value("alwaysOnTop").toBool(true);
    s.pollIntervalSeconds = object.value("pollIntervalSeconds").toDouble(10); s.opacity = object.value("opacity").toDouble(1); s.isExpanded = object.value("isExpanded").toBool(); return s;
}
void SettingsStore::save(const Settings &s) {
    QDir().mkpath(QFileInfo(path()).absolutePath()); QFile file(path()); if (!file.open(QIODevice::WriteOnly | QIODevice::Truncate)) return;
    QJsonObject object{{"x", s.x}, {"y", s.y}, {"alwaysOnTop", s.alwaysOnTop}, {"pollIntervalSeconds", s.pollIntervalSeconds}, {"opacity", s.opacity}, {"isExpanded", s.isExpanded}};
    file.write(QJsonDocument(object).toJson(QJsonDocument::Indented));
}
void ModelDisplayNameRules::load() {
    if (!rules.isEmpty()) return; QFile file(QDir(QCoreApplication::applicationDirPath()).filePath("model-display-names.txt")); if (!file.open(QIODevice::ReadOnly | QIODevice::Text)) return;
    QTextStream stream(&file); while (!stream.atEnd()) { const QString line = stream.readLine().trimmed(); const int pipe = line.indexOf('|'); if (line.isEmpty() || line.startsWith('#') || pipe <= 0) continue; QList<QPair<QString, QString>> pairs;
        for (const QString &pair : line.mid(pipe + 1).split(';', Qt::SkipEmptyParts)) { const int eq = pair.indexOf('='); if (eq > 0) pairs.append({pair.left(eq), pair.mid(eq + 1)}); } if (!pairs.isEmpty()) rules.append({line.left(pipe).trimmed(), pairs}); }
}
QString ModelDisplayNameRules::format(const QString &model) {
    if (model.trimmed().isEmpty()) return "(unknown)"; if (cache.contains(model)) return cache.value(model); load(); QString normalized = model; normalized.replace('-', ' '); QStringList words = normalized.split(' ', Qt::KeepEmptyParts); for (QString &word : words) if (!word.isEmpty()) word = QLocale::c().toUpper(word.left(1)) + word.mid(1); QString result = words.join(' ');
    for (auto it = rules.cbegin(); it != rules.cend(); ++it) if (it->first == "*" || model.startsWith(it->first, Qt::CaseInsensitive)) for (const auto &p : it->second) result.replace(p.first, p.second);
    cache.insert(model, result); return result;
}
DayUsageSnapshot MessageTableRepository::getToday(qint64 startMs) const {
    const QString connection = QString("meter-%1").arg(reinterpret_cast<quintptr>(QThread::currentThreadId()));
    DayUsageSnapshot snapshot{QDateTime::fromMSecsSinceEpoch(startMs).toLocalTime().date().toString(Qt::ISODate), 0, {}, QDateTime::currentDateTime()};
    {
        QSqlDatabase db = QSqlDatabase::addDatabase("QSQLITE", connection);
        db.setDatabaseName(m_path);
        db.setConnectOptions("QSQLITE_OPEN_READONLY;QSQLITE_BUSY_TIMEOUT=2000");
        if (!db.open()) throw std::runtime_error(db.lastError().text().toStdString());
        QSqlQuery query(db);
        query.prepare("SELECT COALESCE(providerID, ''), COALESCE(modelID, ''), COALESCE(SUM(cost), 0) FROM (SELECT json_extract(data, '$.providerID') AS providerID, json_extract(data, '$.modelID') AS modelID, json_extract(data, '$.cost') AS cost FROM message WHERE json_extract(data, '$.role') = 'assistant' AND json_extract(data, '$.time.completed') IS NOT NULL AND CAST(json_extract(data, '$.time.completed') AS INTEGER) >= ? GROUP BY json_extract(data, '$.time.created'), json_extract(data, '$.time.completed')) GROUP BY providerID, modelID ORDER BY SUM(cost) DESC, modelID ASC");
        query.addBindValue(startMs);
        if (!query.exec()) throw std::runtime_error(query.lastError().text().toStdString());
        while (query.next()) { ModelBreakdown row{query.value(0).toString(), query.value(1).toString(), query.value(2).toDouble()}; snapshot.cost += row.cost; snapshot.models.append(row); }
        db.close();
    }
    QSqlDatabase::removeDatabase(connection);
    return snapshot;
}
void UsageWorker::poll(qint64 startMs) { try { emit updated(m_repository.getToday(startMs)); } catch (const std::exception &e) { emit failed(QString::fromUtf8(e.what())); } }
UsagePoller::UsagePoller(const QString &path, double interval, QObject *parent) : QObject(parent), m_worker(new UsageWorker(path)) { qRegisterMetaType<DayUsageSnapshot>(); m_worker->moveToThread(&m_thread); connect(this, &UsagePoller::pollRequested, m_worker, &UsageWorker::poll); connect(m_worker, &UsageWorker::updated, this, [this](DayUsageSnapshot s) { m_inFlight = false; emit updated(std::move(s)); }); connect(m_worker, &UsageWorker::failed, this, [this](const QString &e) { m_inFlight = false; emit failed(e); }); connect(&m_timer, &QTimer::timeout, this, &UsagePoller::tick); setInterval(interval); m_thread.start(); }
UsagePoller::~UsagePoller() { m_timer.stop(); m_thread.quit(); m_thread.wait(); delete m_worker; }
void UsagePoller::start() { if (m_running) return; m_running = true; tick(); m_timer.start(); }
void UsagePoller::setInterval(double seconds) { m_timer.setInterval(qRound(qMax(0.25, seconds) * 1000)); }
qint64 UsagePoller::startOfTodayMs() { return QDateTime(QDate::currentDate(), QTime(0, 0), QTimeZone::systemTimeZone()).toMSecsSinceEpoch(); }
void UsagePoller::tick() { if (m_inFlight) return; m_inFlight = true; emit pollRequested(startOfTodayMs()); }
void UsagePoller::complete() {}
