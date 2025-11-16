* SelectExpr内の匿名型→DTO変換・LINQクエリ自動生成部分を汎用化して、他のプロジェクトでも使えるようにする -> completed #31
* ↑を使用して、以下の機能をもつアナライザーを提供する
  * 現行のSelectクエリ → anonymous type / explicit DTO / predefined DTO SelectExpr への変換機能
  * explicit DTO SelectExpr -> 定義済の型を別ファイル(~~Dto.cs)に切り出し+ predefined DTO SelectExpr / traditional DTO Select への変換機能
  * predefined DTO SelectExpr -> traditional DTO Select への変換機能
* ↑を使用して、オンライン上でデモ的にLinqraftを試せるWebアプリケーションを提供する
  * 例えばBlazor WebAssembly等で実装
