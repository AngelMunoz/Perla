import {
  postEvent as _postEvent,
  getFrameworkOptions as getQunitOptions,
  getFileList,
  getPerlaTestEnv,
  PERLA_SESSION_START,
  PERLA_SUITE_START,
  PERLA_SUITE_END,
  PERLA_TEST_PASS,
  PERLA_TEST_FAILED,
  PERLA_SESSION_END,
  PERLA_TEST_IMPORT_FAILED,
  PERLA_TEST_RUN_FINISHED,
} from "/~perla~/testing/helpers.js";

const [perlaTestingEnv, files, qunitOptions] = await Promise.all([
  getPerlaTestEnv(),
  getFileList(),
  getQunitOptions(),
]);

globalThis.QUnit = {
  config: {
    ...qunitOptions,
    autostart: false,
  },
};

await Promise.all([
  import("qunit"),
  import("qunit/qunit.css", { with: { type: "css" } }),
]).then(([_, QUnitCss]) => document.adoptedStyleSheets.push(QUnitCss.default));

let suiteCount = 0;

function postEvent(event, payload) {
  const id = perlaTestingEnv?.runId;
  return _postEvent(event, id, payload);
}

function createStats(testCounts, runtime = 0) {
  return {
    suites: suiteCount,
    passes: testCounts.passed,
    failures: testCounts.failed,
    pending: testCounts.skipped,
    tests: testCounts.total,
    start: new Date(),
    duration: runtime,
  };
}

QUnit.on("runStart", function (runStart) {
  postEvent(PERLA_SESSION_START, {
    stats: createStats({
      passed: 0,
      failed: 0,
      skipped: 0,
      total: runStart.testCounts.total,
    }),
    totalTests: runStart.testCounts.total,
  });
});

QUnit.on("suiteStart", function (suiteStart) {
  suiteCount++;
  postEvent(PERLA_SUITE_START, {
    stats: createStats({ passed: 0, failed: 0, skipped: 0, total: 0 }),
    suite: {
      id: suiteStart.fullName.join(" > "),
      title: suiteStart.name,
      fullTitle: suiteStart.fullName.join(" > "),
      root: suiteStart.fullName.length === 1,
      parent:
        suiteStart.fullName.length > 1
          ? suiteStart.fullName.slice(0, -1).join(" > ")
          : undefined,
      pending: false,
      tests: [],
    },
  });
});

QUnit.on("suiteEnd", function (suiteEnd) {
  postEvent(PERLA_SUITE_END, {
    stats: createStats(
      { passed: 0, failed: 0, skipped: 0, total: 0 },
      suiteEnd.runtime
    ),
    suite: {
      id: suiteEnd.fullName.join(" > "),
      title: suiteEnd.name,
      fullTitle: suiteEnd.fullName.join(" > "),
      root: suiteEnd.fullName.length === 1,
      parent:
        suiteEnd.fullName.length > 1
          ? suiteEnd.fullName.slice(0, -1).join(" > ")
          : undefined,
      pending: false,
      tests: [],
    },
  });
});

QUnit.on("testEnd", function (testEnd) {
  const test = {
    body: "",
    duration: testEnd.runtime,
    fullTitle: testEnd.fullName.join(" > "),
    id: testEnd.fullName.join(" > "),
    pending: testEnd.status === "skipped",
    speed: undefined,
    state: testEnd.status,
    title: testEnd.name,
    type: "test",
  };

  if (testEnd.status === "passed") {
    postEvent(PERLA_TEST_PASS, {
      stats: createStats(
        { passed: 1, failed: 0, skipped: 0, total: 1 },
        testEnd.runtime
      ),
      test,
    });
  } else if (testEnd.status === "failed") {
    const error = testEnd.errors[0] || {};
    postEvent(PERLA_TEST_FAILED, {
      stats: createStats(
        { passed: 0, failed: 1, skipped: 0, total: 1 },
        testEnd.runtime
      ),
      test,
      message: error.message || "Test failed",
      stack: error.stack || "",
    });
  }
});

QUnit.on("runEnd", function (runEnd) {
  postEvent(PERLA_SESSION_END, {
    stats: createStats(runEnd.testCounts, runEnd.runtime),
  });
});

for (const file of files) {
  try {
    await import(file);
  } catch (err) {
    await postEvent(PERLA_TEST_IMPORT_FAILED, {
      stack: err.stack,
      message: err.message,
    });
  }
}

QUnit.on("runEnd", function () {
  postEvent(PERLA_TEST_RUN_FINISHED, {
    runId: perlaTestingEnv?.runId,
    browser: perlaTestingEnv?.browser,
  });
});

QUnit.start();
