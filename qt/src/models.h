#pragma once

#include <QDateTime>
#include <QList>
#include <QString>

struct ModelBreakdown { QString provider; QString model; double cost = 0; };
struct DayUsageSnapshot { QString dayKey; double cost = 0; QList<ModelBreakdown> models; QDateTime takenAt; };
struct Settings { double x = qQNaN(); double y = qQNaN(); bool alwaysOnTop = true; double pollIntervalSeconds = 10; double opacity = 1; bool isExpanded = false; };
Q_DECLARE_METATYPE(DayUsageSnapshot)
