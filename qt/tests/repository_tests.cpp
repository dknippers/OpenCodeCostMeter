#include "services.h"
#include <QSqlDatabase>
#include <QSqlQuery>
#include <QTemporaryDir>
#include <QtTest>

class RepositoryTests final : public QObject {
    Q_OBJECT
private slots:
    void aggregatesTodaysAssistantMessages();
};

void RepositoryTests::aggregatesTodaysAssistantMessages() {
    QTemporaryDir dir;
    QVERIFY(dir.isValid());
    const QString path = dir.filePath("opencode.db");
    {
        QSqlDatabase db = QSqlDatabase::addDatabase("QSQLITE", "fixture");
        db.setDatabaseName(path);
        QVERIFY(db.open());
        QSqlQuery query(db);
        QVERIFY(query.exec("CREATE TABLE message (data TEXT NOT NULL)"));
        const qint64 start = QDateTime(QDate::currentDate(), QTime(0, 0), Qt::LocalTime).toMSecsSinceEpoch();
        auto insert = [&query](const QString &json) { query.prepare("INSERT INTO message(data) VALUES (?)"); query.addBindValue(json); return query.exec(); };
        QVERIFY(insert(QString("{\"role\":\"assistant\",\"time\":{\"created\":%1,\"completed\":%2},\"providerID\":\"openai\",\"modelID\":\"gpt-test\",\"cost\":0.25}").arg(start + 1).arg(start + 2)));
        QVERIFY(insert(QString("{\"role\":\"assistant\",\"time\":{\"created\":%1,\"completed\":%2},\"providerID\":\"openai\",\"modelID\":\"gpt-test\",\"cost\":0.25}").arg(start + 1).arg(start + 2)));
        QVERIFY(insert(QString("{\"role\":\"assistant\",\"time\":{\"created\":1,\"completed\":%1},\"providerID\":\"anthropic\",\"modelID\":\"claude\",\"cost\":0.5}").arg(start + 3)));
        db.close();
    }
    QSqlDatabase::removeDatabase("fixture");
    MessageTableRepository repository(path);
    const DayUsageSnapshot snapshot = repository.getToday(QDateTime(QDate::currentDate(), QTime(0, 0), Qt::LocalTime).toMSecsSinceEpoch());
    QCOMPARE(snapshot.cost, 0.75);
    QCOMPARE(snapshot.models.size(), 2);
    QCOMPARE(snapshot.models.at(0).model, "claude");
}

QTEST_MAIN(RepositoryTests)
#include "repository_tests.moc"
