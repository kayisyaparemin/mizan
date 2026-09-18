---
title: Görev Panosu ve Yol Haritası
type: moc
updated: 2026-09-18
tags:
  - moc
  - backlog
  - roadmap
---

# 📋 Görev Panosu ve Yol Haritası (Backlog & Roadmap)

Mizan projesinde planlanan, devam eden ve tamamlanan tüm geliştirme işlerinin mimari ağa bağlı takip panosu.

---

## 🚀 Planlanan Görevler (Backlog)

```dataview
TABLE status AS "Durum", priority AS "Öncelik", pillar AS "Taşıyıcı Sütun", file.outlinks AS "Etkilenen Kodlar ve Kurallar"
FROM #task
WHERE status = "planned"
SORT priority DESC
```

---

## ⚡ Devam Eden Görevler (In Progress)

```dataview
TABLE priority AS "Öncelik", pillar AS "Taşıyıcı Sütun", file.outlinks AS "Etkilenen Kodlar"
FROM #task
WHERE status = "in-progress"
SORT priority DESC
```

---

## ✅ Tamamlananlar (Done)

```dataview
TABLE pillar AS "Taşıyıcı Sütun", file.outlinks AS "Dokunulan Kodlar"
FROM #task
WHERE status = "done"
SORT file.name ASC
```

---

## 📑 Statik Görev Listesi (Dataview Olmayan Durumlar İçin)

- [[TASK-01 - Google Auth ve Offline-First Abonelik Altyapisi|TASK-01: Google Auth ve Offline-First Abonelik Altyapısı]] — `status: planned` | `priority: high`
- [[TASK-02 - Otomatik Regresyon Test Paketi ve CI-CD Kalite Kapisi|TASK-02: Otomatik Regresyon Test Paketi ve CI-CD Kalite Kapısı]] — `status: planned` | `priority: high`
