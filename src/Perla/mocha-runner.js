import {
  postEvent as _postEvent,
  getFileList,
  getPerlaTestEnv,
  getFrameworkOptions as getMochaSettings,
  PERLA_SESSION_START,
  PERLA_SUITE_START,
  PERLA_SUITE_END,
  PERLA_TEST_PASS,
  PERLA_TEST_FAILED,
  PERLA_SESSION_END,
  PERLA_TEST_IMPORT_FAILED,
  PERLA_TEST_RUN_FINISHED,
} from "/~perla~/testing/helpers.js";

await Promise.all([
  import("mocha"),
  import("mocha/mocha.css", { with: { type: "css" } }),
]).then(([_, MochaCss]) => document.adoptedStyleSheets.push(MochaCss.default));

const {
  EVENT_RUN_BEGIN,
  EVENT_RUN_END,
  EVENT_TEST_FAIL,
  EVENT_TEST_PASS,
  EVENT_SUITE_BEGIN,
  EVENT_SUITE_END,
} = Mocha.Runner.constants;

const [perlaTestingEnv, mochaSettings, files] = await Promise.all([
  getPerlaTestEnv(),
  getMochaSettings(),
  getFileList(),
]).then(([env, settings, fileList]) => [env, settings ?? {}, fileList]);

function postEvent(event, payload) {
  const id = perlaTestingEnv?.runId;
  return _postEvent(event, id, payload);
}

function serializeTest(test) {
  const serialized = test.serialize();
  return {
    body: serialized.body,
    duration: serialized.duration,
    fullTitle: serialized.$$fullTitle,
    speed: serialized.speed,
    id: test.id,
    pending: test.pending,
    state: serialized.state,
    title: serialized.title,
    type: serialized.type,
  };
}

function serializeSuite(suite) {
  return {
    id: suite.id,
    title: suite.title,
    fullTitle: suite.fullTitle(),
    root: suite.root,
    parent: suite.parent?.id,
    pending: suite.pending,
    tests: suite.tests.map(serializeTest),
  };
}

function getSuiteAndStats(stats, suite) {
  return { stats: { ...stats }, suite: serializeSuite(suite) };
}

function MyReporter(runner, options) {
  Mocha.reporters.HTML.call(this, runner, options);

  runner
    .once(EVENT_RUN_BEGIN, function notifyStart() {
      postEvent(PERLA_SESSION_START, {
        stats: { ...this.stats },
        totalTests: this.total,
      });
    })
    .on(EVENT_SUITE_BEGIN, function notifySuiteStart(suite) {
      postEvent(PERLA_SUITE_START, getSuiteAndStats(this.stats, suite));
    })
    .on(EVENT_SUITE_END, function notifySuiteEnd(suite) {
      postEvent(PERLA_SUITE_END, getSuiteAndStats(this.stats, suite));
    })
    .on(EVENT_TEST_PASS, function notifyPass(test) {
      postEvent(PERLA_TEST_PASS, {
        stats: { ...this.stats },
        test: serializeTest(test),
      });
    })
    .on(EVENT_TEST_FAIL, function notifyFail(test, err) {
      postEvent(PERLA_TEST_FAILED, {
        stats: { ...this.stats },
        test: serializeTest(test),
        message: err.message,
        stack: err.stack,
      });
    })
    .once(EVENT_RUN_END, function notifyEnd() {
      postEvent(PERLA_SESSION_END, { stats: { ...this.stats } });
    });
}

Mocha.utils.inherits(MyReporter, Mocha.reporters.HTML);

if (!mochaSettings.ui) {
  mochaSettings.ui = "bdd"; // Default to BDD if no UI is specified
}
mocha.setup({
  reporter: MyReporter,
  ...mochaSettings,
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

mocha.run(() => {
  postEvent(PERLA_TEST_RUN_FINISHED, { runId: perlaTestingEnv?.runId });
});
