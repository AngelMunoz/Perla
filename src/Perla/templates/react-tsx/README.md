# Javascript + Perla

[perla]: https://github.com/AngelMunoz/Perla

Visit the Perla Docs at https://perla-docs.web.app

## Getting Started

> If you don't have [Perla] installed, follow the instructions here
> https://perla-docs.web.app/#/content/install

## Development

open a terminal into your project directory and simply type

- `perla serve`

## Build

open a terminal into your project directory and simply type

- `perla build`

## Install dependencies

open a terminal into your project directory and simply type

- `perla add -p <Dependency>`, example: `perla add -p lodash`
- `perla add -p lodash@3 --alias lodash-three`

you will be able to import as the following in your code

```javascript
import _ from "lodash";
import _3 from "lodash-three";
```

To remove dependencies just change `add` to `remove`

- `perla remove -p <Dependency>`, example: `perla remove -p lodash`
- `perla remove -p lodash@3`
