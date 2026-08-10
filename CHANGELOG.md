# Changelog

## 1.0.0 (2026-08-10)


### Features

* add opentelemetry ([b326804](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/b32680460e3ea4399c8562549793b6749d8e7ea0))
* add unit/integration tests, refactor position calculators, fix sln ([b15c9ae](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/b15c9aec1345c46db8cc562e1a962ac8349c181e))
* flag for logging ([e171df0](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/e171df04c9699356ba392c2feec54c22ca816ca6))
* publisher confirms, durable DLQ topology, and health checks ([b38066f](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/b38066f36fa7229c9fc392c9d48a4e769974e648))
* remove weak exchange ([45c1083](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/45c108317b0c16a15582ca63c5db114665bfca7a))


### Bug Fixes

* **ci:** address SonarQube findings on release.yml ([eb641e8](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/eb641e8c880bfcc5d3a54cbfe0ad2e4f7e388e04))
* correct invalid JSON in appsettings.json proxy port field ([6780ff9](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/6780ff9f3c1e150c3ee05a37dc9a4921e3051763))
* deploy ([93e5599](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/93e55990c814cd28c994074b8640b2a4e2508104))
* deploy fix ([2655033](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/2655033d4a379d5b9925b3a1d74e4ad202ae0587))
* deploy pipeline ([b3a864c](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/b3a864c65cbded9c32406d0af89327965b021dbc))
* exchange block ([869b9fb](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/869b9fb92b6c21ee8932bca3c85fd928aecc571e))
* exchange list config ([05ee449](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/05ee449038723c09d127accc84a95fafbe3aa65f))
* honor shutdown cancellation in ArbitrageService's main loops ([a70333b](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/a70333bbce5c1dfcb47115fc3cb27598807c9274))
* honor shutdown cancellation in funding watch loop ([24e5189](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/24e5189a90044ba6498b5dc2df0591b69ab3c237))
* honor shutdown cancellation in futures watch loop, merge redundant if ([f418ca5](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/f418ca5c957148c5a4a35683c0d15f14b0e13c05))
* honor shutdown cancellation in spot watch loop ([492ca84](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/492ca84044bfed93e0c6b83e892ccc0b16ae94eb))
* pipeline ([e1bac6e](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/e1bac6ebd6ceb5d02ccc94cb26554a746dba511d))
* remove dead/redundant checks flagged by SonarQube ([6f9cfd3](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/6f9cfd3d095d343d584b0f8fbefabafb3f7806db))
* stop churning HttpClient per proxy rotation in ProxyService ([c88612c](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/c88612ccc71adb2323cc157d5403fbc0f4abb55f))
* stop mutating static fields from instance constructors ([225c830](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/225c830a9da83487832be8ad49ade268a5431578))
* throw InvalidOperationException instead of bare Exception ([58e5f63](https://github.com/dimasdom/ArbitrageSpreadScanner/commit/58e5f63ade827b0ba2dd0ef63537a62fb25a9c8e))
