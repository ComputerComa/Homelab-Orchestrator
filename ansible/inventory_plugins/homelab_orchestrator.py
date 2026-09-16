from __future__ import annotations

DOCUMENTATION = r"""
    name: homelab_orchestrator
    short_description: Inventory from Homelab Orchestrator's inventory API
    description:
        - Queries one of Homelab Orchestrator's read-only Ansible inventory endpoints
          (C(GET /api/inventory/containers/{vmid}) or C(GET /api/inventory/containers/running))
          and populates Ansible inventory straight from the returned C(_meta.hostvars) document.
        - Every hostvar the API returns (C(ansible_host), C(ansible_user), C(ansible_port),
          C(ansible_ssh_private_key_file), C(tags)) is set on the host as-is.
    author: Homelab Orchestrator
    options:
        plugin:
            description: Token that ensures this is a source file for the C(homelab_orchestrator) plugin.
            required: true
            type: str
            choices: ['homelab_orchestrator']
        api_url:
            description: Base URL of the Homelab Orchestrator instance, e.g. V(http://localhost:5050).
            required: true
            type: str
        mode:
            description:
                - V(running) fetches C(GET /api/inventory/containers/running) (every running container).
                - V(vmid) fetches C(GET /api/inventory/containers/{vmid}) (one specific container).
            type: str
            choices: ['running', 'vmid']
            default: running
        vmid:
            description: The container VMID to query. Required when O(mode=vmid).
            type: int
        validate_certs:
            description: Whether to validate TLS certificates when O(api_url) is C(https).
            type: bool
            default: true
        timeout:
            description: HTTP request timeout in seconds.
            type: int
            default: 10
    extends_documentation_fragment:
        - constructed
"""

EXAMPLES = r"""
# ansible/inventory/orchestrator.yml — every running container, grouped by tag
plugin: homelab_orchestrator
api_url: http://localhost:5050
mode: running
keyed_groups:
  - key: tags
    prefix: tag

# ansible/inventory/orchestrator-single.yml — one container by VMID
plugin: homelab_orchestrator
api_url: http://localhost:5050
mode: vmid
vmid: 141
"""

import json

import yaml
from ansible.errors import AnsibleParserError
from ansible.module_utils.urls import ConnectionError, SSLValidationError, open_url
from ansible.plugins.inventory import BaseInventoryPlugin, Constructable

try:
    from urllib.error import HTTPError, URLError
except ImportError:  # pragma: no cover - Python 2 fallback, ansible-core itself requires Python 3
    from urllib2 import HTTPError, URLError


class InventoryModule(BaseInventoryPlugin, Constructable):
    """Dynamic inventory sourced from Homelab Orchestrator's inventory API."""

    NAME = "homelab_orchestrator"

    def verify_file(self, path):
        if not super().verify_file(path):
            return False
        if not path.endswith((".yml", ".yaml")):
            return False
        try:
            with open(path, "r") as source:
                config = yaml.safe_load(source)
        except (OSError, yaml.YAMLError):
            return False
        return isinstance(config, dict) and config.get("plugin") == self.NAME

    def parse(self, inventory, loader, path, cache=True):
        super().parse(inventory, loader, path, cache=cache)
        self._read_config_data(path)

        api_url = self.get_option("api_url").rstrip("/")
        mode = self.get_option("mode")
        validate_certs = self.get_option("validate_certs")
        timeout = self.get_option("timeout")

        if mode == "vmid":
            vmid = self.get_option("vmid")
            if vmid is None:
                raise AnsibleParserError("homelab_orchestrator: mode=vmid requires the 'vmid' option to be set")
            url = f"{api_url}/api/inventory/containers/{vmid}"
        else:
            url = f"{api_url}/api/inventory/containers/running"

        document = self._fetch_inventory(url, validate_certs, timeout)
        self._populate(document)

    def _fetch_inventory(self, url, validate_certs, timeout):
        try:
            response = open_url(
                url,
                method="GET",
                headers={"Accept": "application/json"},
                validate_certs=validate_certs,
                timeout=timeout,
            )
            body = response.read()
        except HTTPError as error:
            if error.code == 404:
                raise AnsibleParserError(
                    f"homelab_orchestrator: no such container at {url} (HTTP 404)"
                ) from error
            raise AnsibleParserError(
                f"homelab_orchestrator: Homelab Orchestrator returned HTTP {error.code} for {url}: {error.reason}"
            ) from error
        except (URLError, ConnectionError, SSLValidationError) as error:
            raise AnsibleParserError(f"homelab_orchestrator: could not reach {url}: {error}") from error
        except Exception as error:
            raise AnsibleParserError(f"homelab_orchestrator: error fetching inventory from {url}: {error}") from error

        try:
            return json.loads(body)
        except ValueError as error:
            raise AnsibleParserError(f"homelab_orchestrator: invalid JSON returned from {url}: {error}") from error

    def _populate(self, document):
        if not isinstance(document, dict):
            raise AnsibleParserError("homelab_orchestrator: expected a JSON object from the inventory API")

        hostvars = document.get("_meta", {}).get("hostvars")
        if hostvars is None:
            raise AnsibleParserError(
                "homelab_orchestrator: response is missing the expected _meta.hostvars structure"
            )

        all_group = document.get("all", {})
        hosts = all_group.get("hosts", list(hostvars.keys()))
        strict = self.get_option("strict")

        for host in hosts:
            self.inventory.add_host(host, group="all")

            for var_name, var_value in hostvars.get(host, {}).items():
                self.inventory.set_variable(host, var_name, var_value)

            variables = self.inventory.get_host(host).get_vars()
            self._set_composite_vars(self.get_option("compose"), variables, host, strict=strict)
            self._add_host_to_composed_groups(self.get_option("groups"), variables, host, strict=strict)
            self._add_host_to_keyed_groups(self.get_option("keyed_groups"), variables, host, strict=strict)
