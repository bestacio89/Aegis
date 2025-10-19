# 🛡️ POLITIQUE AEGIS — NIVEAU DE CONFORMITÉ BERNARDO

**Version :** 1.0.0  
**Mainteneur :** Bernardo Estacio Abreu  
**Mode :** *Audit Sniper — Rigueur Architecturale Totale*

---

## 🎯 Objectif

Ce fichier définit la **politique centrale de gouvernance** utilisée par le système intelligent d’évaluation **AEGIS**.  
Il reflète la philosophie architecturale de Bernardo Estacio Abreu :  
une vision où la rigueur, la cohérence et la lisibilité du code priment sur toute forme d’improvisation.

Chaque paramètre a été conçu pour :

- Prévenir toute dérive architecturale.  
- Éliminer les modèles anémiques et les “God Classes”.  
- Garantir la maintenabilité et la robustesse du code.  
- Favoriser la reproductibilité, la traçabilité et la scalabilité.  

---

## ⚙️ Philosophie d’Application

| Niveau | Définition | Comportement AEGIS |
|--------|-------------|--------------------|
| **Strict** | Les violations déclenchent des avertissements ou erreurs avec conseils de correction. | Vérification immédiate et bloquante. |
| **Advisory** | Conseils d’amélioration sans sanction. | Évaluation passive et informative. |
| **Sniper** | Mode hybride intelligent : tolérance zéro, contexte compris, aucun faux positif. | Activé dans cette configuration. |

---

## 🧩 Domaines d’Évaluation

| Catégorie | Objectif | Mode |
|------------|-----------|------|
| **Maintenabilité** | Garantir la lisibilité et la documentation à long terme. | 🔒 *Strict* |
| **Complexité** | Favoriser la granularité et le découplage fonctionnel. | 🔒 *Strict* |
| **Architecture** | Préserver la pureté des couches `Api → App → Domain → Infra`. | 🔒 *Strict* |
| **Graphes de dépendances** | Identifier couplages excessifs et dépendances circulaires. | 🔒 *Strict* |
| **Transactions** | Imposer les commits et rollbacks explicites. | 🔒 *Strict* |
| **Performance** | Promouvoir l’asynchronisme et la scalabilité. | 🔒 *Strict* |
| **Sécurité** | Interdire tout secret ou chiffrement faible. | 🔒 *Strict* |
| **Design Patterns** | Faire respecter les patrons de conception reconnus. | 🎯 *Sniper* |
| **Anti-Patterns** | Détecter les structures déviantes (God Class, Domaines Anémiques, etc.). | 🔒 *Strict* |

---

## 🧠 Couverture des Design Patterns

| Patron | Enforcé | Règle |
|--------|----------|--------|
| **Repository** | ✅ | Interface requise, cohérence asynchrone. |
| **Singleton** | ✅ | Thread-safe, initialisation paresseuse, instance unique. |
| **Factory** | ✅ | ≤ 3 instanciations par classe. |
| **Builder** | ✅ | Doit exposer une interface fluide + méthode `Build()`. |
| **Mediator** | ✅ (.NET uniquement) | Compatible `MediatR` et `Franz.Common.Mediator`. |
| **Command** | ✅ | Doit posséder un `Handler` correspondant + Undo optionnel. |
| **Strategy** | ✅ | Interface requise, pas de logique conditionnelle. |
| **Observer** | ✅ | Gestion propre des abonnements et désabonnements. |
| **Decorator** | ✅ | Injection du composant décoré via le constructeur. |
| **Facade** | ✅ | ≤ 4 dépendances, ≤ 8 méthodes publiques. |

---

## 💀 Détection d’Anti-Patterns

| Type | Seuil | Niveau |
|------|--------|--------|
| **God Class** | ≥ 20 méthodes ou ≥ 600 lignes | 🔴 Erreur |
| **Modèle Anémique** | ≥ 70 % de propriétés / ≤ 1 méthode | 🟠 Avertissement |
| **Dépendances circulaires** | Profondeur > 8 | 🔴 Erreur |
| **Médiateur surchargé** | > 10 appels médiateur/fichier | 🟠 Avertissement |

---

## 📦 Hygiène des Dépôts

| Exigence | Règle |
|-----------|--------|
| Fichiers de gouvernance | `README.md`, `.editorconfig`, `LICENSE` obligatoires |
| CI/CD | Présence requise d’un flux (`.github/workflows` ou `azure-pipelines.yml`) |
| Taille maximale d’un fichier | ≤ 10 Mo |
| Densité de TODO/FIXME | ≤ 3 occurrences par fichier |

---

## ⚡ Contraintes de Performance

| Règle | Description |
|--------|-------------|
| Aucune opération bloquante | (`Thread.Sleep`, `.Result`, `Task.Wait`, etc.) |
| Profondeur de boucle maximale | ≤ 3 |
| Taille de corps de boucle | ≤ 80 lignes |
| I/O asynchrone obligatoire | Pour toute opération fichier/réseau |
| Collections volumineuses | Avertissement ≥ 1000 éléments |

---

## 🔒 Contraintes de Sécurité

- Détection automatique de secrets codés en dur.  
- Refus des algorithmes faibles (`MD5`, `SHA1`).  
- Enforcement des protocoles SSL/TLS ≥ 1.2.  

---

## 🧰 Indicateurs de Maintenabilité

| Indicateur | Seuil |
|-------------|--------|
| Indice de maintenabilité | ≥ 80 |
| Densité de commentaires | ≥ 8 % |
| Taille max. d’un fichier | ≤ 400 lignes |
| Poids de la complexité | 2.5 |
| Poids par ligne | 0.015 |

---

## 🧬 Philosophie "Bernardo Style"

> *La précision n’est pas la sévérité, c’est le respect du métier.*

Cette politique fait d’AEGIS un **analyste déterministe**,  
un outil d’ingénierie de la qualité architecturale,  
capable de diagnostiquer sans biais et d’imposer des standards sans compromis.  

Chaque règle est mesurable, rationnelle, et issue d’une logique d’excellence technique.

---

### ✅ Résumé

- **Mode :** Audit Sniper  
- **Langages :** .NET (C#), Java, Python, TypeScript  
- **Objectif :** Uniformisation architecturale, performance et qualité  
- **Philosophie :** Cohérence prévisible → Scalabilité maîtrisée  

---

> **Devise AEGIS :**  
> “Aucune architecture ne doit dériver sans être observée.”
