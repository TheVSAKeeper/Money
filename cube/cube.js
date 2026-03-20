module.exports = {
  queryRewrite: (query, { securityContext }) => {
    const userId = securityContext?.userId;
    if (!userId) return query;

    const cubeNames = new Set([
      ...(query.measures || []).map(m => m.split('.')[0]),
      ...(query.dimensions || []).map(d => d.split('.')[0]),
      ...(query.filters || []).map(f => f.member?.split('.')[0]).filter(Boolean),
    ]);

    const systemCubes = new Set(['api_metrics']);

    for (const cubeName of cubeNames) {
      if (systemCubes.has(cubeName)) continue;

      query.filters = (query.filters || []).concat({
        member: `${cubeName}.user_id`,
        operator: 'equals',
        values: [String(userId)],
      });
    }

    return query;
  },
  contextToAppId: ({ securityContext }) =>
    `CUBE_APP_${securityContext?.userId || 'system'}`,
};
