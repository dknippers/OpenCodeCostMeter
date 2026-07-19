#pragma once

#include "models.h"
#include <QHash>
#include <QObject>
#include <QSqlDatabase>
#include <QThread>
#include <QTimer>

class DbLocator { public: static QString defaultPath(); static QString resolve(const QString &overridePath); };
class SettingsStore { public: Settings load(); void save(const Settings &settings); private: QString path() const; };
class ModelDisplayNameRules { public: static QString format(const QString &model); private: static void load(); static QHash<QString, QString> cache; static QList<QPair<QString, QList<QPair<QString, QString>>>> rules; };

class MessageTableRepository {
public:
    explicit MessageTableRepository(QString path) : m_path(std::move(path)) {}
    DayUsageSnapshot getToday(qint64 startMs) const;
private: QString m_path;
};

class UsageWorker final : public QObject {
    Q_OBJECT
public: explicit UsageWorker(QString databasePath) : m_repository(std::move(databasePath)) {}
public slots: void poll(qint64 startMs);
signals: void updated(DayUsageSnapshot snapshot); void failed(QString error);
private: MessageTableRepository m_repository;
};

class UsagePoller final : public QObject {
    Q_OBJECT
public:
    explicit UsagePoller(const QString &databasePath, double interval, QObject *parent = nullptr);
    ~UsagePoller() override;
    void start(); void setInterval(double seconds);
    static qint64 startOfTodayMs();
signals: void updated(DayUsageSnapshot snapshot); void failed(QString error);
private slots: void tick(); void complete();
private:
    QTimer m_timer; QThread m_thread; UsageWorker *m_worker; bool m_running = false; bool m_inFlight = false;
signals: void pollRequested(qint64 startMs);
};
