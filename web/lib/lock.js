'use strict';
// Runs display-changing work one piece at a time, in the order it was asked for. A failure in one piece does
// not stop the ones queued behind it.

let tail = Promise.resolve();

function exclusive(fn) {
  const run = tail.then(() => fn());
  tail = run.then(() => {}, () => {});
  return run;
}

module.exports = { exclusive };
