const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');
const React = require('react');
const PropTypes = require('prop-types');
const { transformSync } = require('@babel/core');

const componentFile = path.resolve(__dirname, 'BidiText.js');
const helperFile = path.resolve(__dirname, '../Utilities/String/isolateBidiText.js');

function loadModule(file, dependencies = {}, plugins = [], presets = []) {
  const { code } = transformSync(fs.readFileSync(file, 'utf8'), {
    filename: file,
    babelrc: false,
    configFile: false,
    plugins: ['@babel/plugin-transform-modules-commonjs', ...plugins],
    presets
  });
  const exports = {};

  vm.runInNewContext(code, {
    exports,
    require(name) {
      if (Object.hasOwn(dependencies, name)) {
        return dependencies[name];
      }
      throw new Error(`Unexpected BidiText dependency: ${name}`);
    }
  }, { filename: file });

  return exports;
}

const helperExports = loadModule(helperFile);
const componentExports = loadModule(componentFile, {
  react: React,
  'prop-types': PropTypes,
  'Utilities/String/isolateBidiText': helperExports
}, [], ['@babel/preset-react']);

test('BidiText loads the shared sanitizer and removes hostile directional controls', () => {
  const external = 'Book\u2069\u202Eapproved\u202C\u206B';
  const element = componentExports.default({
    children: React.createElement('span', null, external),
    title: external,
    'aria-label': external
  });

  assert.equal(typeof helperExports.sanitizeBidiText, 'function');
  assert.equal(element.type, 'bdi');
  assert.equal(element.props.children.props.children, 'Bookapproved');
  assert.equal(element.props.title, 'Bookapproved');
  assert.equal(element.props['aria-label'], 'Bookapproved');
});

test('BidiText preserves balanced isolates only when requested', () => {
  const balanced = '\u2068Book\u2069';

  assert.equal(helperExports.sanitizeBidiText(`${balanced}\u202E`, true), balanced);
  assert.equal(helperExports.sanitizeBidiText('Book\u2069approved', true), 'Bookapproved');
  assert.equal(helperExports.sanitizeBidiText('Book\u2068approved', true), 'Bookapproved');
});
