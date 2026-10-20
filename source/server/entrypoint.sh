#!/bin/sh
# Container entry point (TDD §3.1a). With SW_NETEM set (B2: "delay 75ms loss 2%"), shapes this container's outgoing traffic with
# tc netem first; the container needs the NET_ADMIN capability for that. Then runs the Godot project headless with the arguments.
set -e
if [ -n "${SW_NETEM}" ]; then
  tc qdisc replace dev eth0 root netem ${SW_NETEM}
  echo "{\"event\":\"netem.applied\",\"message\":\"eth0: ${SW_NETEM}\"}"
fi
exec godot --headless --path /src/project-sumoner-worl -- "$@"
