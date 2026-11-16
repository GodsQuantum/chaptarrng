const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { transformSync } = require('@babel/core');

const propType = () => propType;
propType.isRequired = propType;

const PropTypes = new Proxy({}, {
  get() {
    return propType;
  }
});

const styles = new Proxy({}, {
  get(_target, property) {
    return property.toString();
  }
});

function loadMetadataProvider() {
  const states = [];
  let stateIndex = 0;
  const React = {
    Fragment: 'Fragment',
    createElement(type, props, ...children) {
      return {
        type,
        props: {
          ...props,
          children: children.length === 1 ? children[0] : children
        }
      };
    }
  };
  const useState = (initialValue) => {
    const index = stateIndex++;
    if (!(index in states)) {
      states[index] = initialValue;
    }
    return [states[index], (value) => {
      states[index] = value;
    }];
  };

  const filePath = path.join(__dirname, 'MetadataProvider.js');
  const source = fs.readFileSync(filePath, 'utf8');
  const { code } = transformSync(source, {
    filename: filePath,
    babelrc: false,
    configFile: false,
    presets: [
      ['@babel/preset-env', { targets: { node: 'current' }, modules: 'commonjs' }],
      '@babel/preset-react'
    ]
  });
  const modules = new Map([
    ['react', { __esModule: true, default: React, useState }],
    ['prop-types', { __esModule: true, default: PropTypes }],
    ['Components/Alert', { __esModule: true, default: 'Alert' }],
    ['Components/FieldSet', { __esModule: true, default: 'FieldSet' }],
    ['Components/Form/Form', { __esModule: true, default: 'Form' }],
    ['Components/Form/FormGroup', { __esModule: true, default: 'FormGroup' }],
    ['Components/Form/FormInputGroup', { __esModule: true, default: 'FormInputGroup' }],
    ['Components/Form/FormLabel', { __esModule: true, default: 'FormLabel' }],
    ['Components/Link/Button', { __esModule: true, default: 'Button' }],
    ['Components/Loading/LoadingIndicator', { __esModule: true, default: 'LoadingIndicator' }],
    ['Helpers/Props', {
      inputTypes: { CHECK: 'check', PASSWORD: 'password', SELECT: 'select', TEXT: 'text', TEXT_AREA: 'text-area' },
      kinds: { DEFAULT: 'default', DANGER: 'danger' }
    }],
    ['System/Quickstart/HardcoverApiKeyModal', { __esModule: true, default: 'HardcoverApiKeyModal' }],
    ['Utilities/String/translate', { __esModule: true, default: (key) => key }],
    ['./MetadataProvider.css', { __esModule: true, default: styles }]
  ]);
  const module = { exports: {} };
  vm.runInNewContext(code, {
    module,
    exports: module.exports,
    require(name) {
      assert.ok(modules.has(name), `Unexpected module: ${name}`);
      return modules.get(name);
    }
  }, { filename: filePath });

  const component = module.exports.default || module.exports;
  return {
    render(props) {
      stateIndex = 0;
      return component(props);
    }
  };
}

function findElements(element, predicate, matches = []) {
  if (Array.isArray(element)) {
    element.forEach((child) => findElements(child, predicate, matches));
    return matches;
  }
  if (!element || typeof element !== 'object') {
    return matches;
  }
  if (predicate(element)) {
    matches.push(element);
  }
  findElements(element.props?.children, predicate, matches);
  return matches;
}

function findText(element, text, matches = []) {
  if (Array.isArray(element)) {
    element.forEach((child) => findText(child, text, matches));
  } else if (element && typeof element === 'object') {
    if (element === text) {
      matches.push(element);
    }
    findText(element.props?.children, text, matches);
  } else if (element === text) {
    matches.push(element);
  }
  return matches;
}

function makeProps({ hasToken = false, tokenFromEnvironment = false } = {}) {
  const settings = new Proxy({
    hasHardcoverAuth: { value: hasToken },
    hardcoverAuthFromEnvironment: { value: tokenFromEnvironment },
    sourcesFromEnvironment: { value: false }
  }, {
    get(target, name) {
      return Object.prototype.hasOwnProperty.call(target, name) ? target[name] : { value: false };
    }
  });

  return {
    isFetching: false,
    error: null,
    settings,
    hasSettings: true,
    onInputChange: () => null
  };
}

test('Metadata settings connect to Hardcover and refresh credential status', () => {
  const component = loadMetadataProvider();
  const props = makeProps();
  const tree = component.render(props);
  const button = findElements(tree, (element) => element.type === 'Button')
    .find((element) => element.props.children === 'ConnectHardcover');

  assert.ok(button, 'the Metadata page should offer Hardcover connection beside its source option');
  const modal = findElements(tree, (element) => element.type === 'HardcoverApiKeyModal')[0];
  assert.equal(modal.props.isOpen, false);
  button.props.onPress();

  const openTree = component.render(props);
  const openModal = findElements(openTree, (element) => element.type === 'HardcoverApiKeyModal')[0];
  assert.equal(openModal.props.isOpen, true);
  openModal.props.onConnectionSuccess();
  const connectedTree = component.render(props);
  assert.ok(findElements(connectedTree, (element) => element.type === 'Button')
    .some((element) => element.props.children === 'ConfigureHardcoverApi'));
  assert.ok(findText(connectedTree, 'MetadataProviderKeySaved').length > 0);
});

test('Metadata settings show saved and environment-provided Hardcover token states', () => {
  const savedTree = loadMetadataProvider().render(makeProps({ hasToken: true }));
  assert.ok(findElements(savedTree, (element) => element.type === 'Button')
    .some((element) => element.props.children === 'ConfigureHardcoverApi'));
  assert.ok(findText(savedTree, 'MetadataProviderKeySaved').length > 0);

  const environmentTree = loadMetadataProvider().render(makeProps({ hasToken: true, tokenFromEnvironment: true }));
  const environmentButton = findElements(environmentTree, (element) => element.type === 'Button')
    .find((element) => element.props.children === 'ConfigureHardcoverApi');
  assert.ok(environmentButton);
  assert.equal(environmentButton.props.isDisabled, true);
  assert.ok(findText(environmentTree, 'MetadataProviderCredentialEnvironmentOverride').length > 0);
});
